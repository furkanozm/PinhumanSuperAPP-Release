using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.IO;
using OfficeOpenXml;
using WebScraper;
using Microsoft.Playwright;
using OtpNet;

namespace WebScraper
{
    public partial class PersonnelWindow : Window
    {
        private PersonnelConfig _config;
        private PersonnelHistory _personnelHistory;
        private System.Windows.Threading.DispatcherTimer _clockTimer;

        // Sözleşmeli personel modu için
        private bool _isSözleşmeliPersonelMode = false;

        // Personel ekleme için gerekli field'lar
        private IBrowser _browser;
        private IPage _page;
        private List<UIElement> _formFields = new List<UIElement>();
        private List<Dictionary<string, string>> _excelData;
        private bool _useExcelData = false;
        private string _excelFilePath;

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

            // Tab 2 - Ekstra Bilgiler
            new PersonnelField { FieldName = "HasIskurRecord", DisplayName = "İŞKUR Kaydı", Selector = "select[name='HasIskurRecord']", IsRequired = true, InputType = "select", Options = new[] { "true", "false" }, DefaultValue = "true", TabIndex = 2 },
            new PersonnelField { FieldName = "BirthDate", DisplayName = "Doğum Tarihi", Selector = "input[name='BirthDate']", IsRequired = false, InputType = "date", TabIndex = 2 },
            new PersonnelField { FieldName = "Phone", DisplayName = "Tel. No", Selector = "input[name='Phone']", IsRequired = false, InputType = "text", TabIndex = 2 },
            new PersonnelField { FieldName = "Email", DisplayName = "E-Posta", Selector = "input[name='Email']", IsRequired = false, InputType = "text", TabIndex = 2 },
            new PersonnelField { FieldName = "EducationLevel", DisplayName = "Öğrenim Durumu", Selector = "select[name='EducationLevel']", IsRequired = false, InputType = "select", TabIndex = 2 },
            new PersonnelField { FieldName = "IsDisabled", DisplayName = "Engelli", Selector = "select[name='IsDisabled']", IsRequired = false, InputType = "select", Options = new[] { "true", "false" }, TabIndex = 2 },
            new PersonnelField { FieldName = "DisabilityDescription", DisplayName = "Engel Açıklaması", Selector = "input[name='DisabilityDescription']", IsRequired = false, InputType = "text", TabIndex = 2 },
            new PersonnelField { FieldName = "SpecialConditions", DisplayName = "Özel Durumlar", Selector = "input[name='SpecialConditions']", IsRequired = false, InputType = "text", TabIndex = 2 },
            new PersonnelField { FieldName = "CardId", DisplayName = "Kart Id", Selector = "input[name='CardId']", IsRequired = false, InputType = "text", TabIndex = 2 },
            new PersonnelField { FieldName = "CardNo", DisplayName = "Kart No", Selector = "input[name='CardNo']", IsRequired = false, InputType = "text", TabIndex = 2 },
            new PersonnelField { FieldName = "ProvinceId", DisplayName = "İl", Selector = "select[name='CityId']", IsRequired = false, InputType = "select", TabIndex = 2 },
            new PersonnelField { FieldName = "DistrictId", DisplayName = "İlçe", Selector = "select[name='CountyId']", IsRequired = false, InputType = "select", TabIndex = 2 },
            new PersonnelField { FieldName = "Subcontractors", DisplayName = "Alt Yüklenici Listesi", Selector = ".row.row-cols-4", IsRequired = false, InputType = "subcontractors", TabIndex = 2 },

            // Tab 3 - Banka Bilgileri
            new PersonnelField { FieldName = "BANKA", DisplayName = "BANKA", Selector = "#BankAccountCEViewModel_BankId", IsRequired = true, InputType = "select", Options = new[] { "ZİRAAT BANKASI", "HALK BANKASI", "VAKIF BANK", "GARANTİ BANKASI", "AKBANK", "YAPIKREDİ", "İŞ BANKASI", "TÜRK EKONOMİ BANKASI (TEB)", "DENİZ BANK", "İNG BANK", "QNB FİNANSBANK", "Diğer" }, TabIndex = 3 },
            new PersonnelField { FieldName = "HESAP ADI", DisplayName = "HESAP ADI", Selector = "input[name='BankAccountCEViewModel.AccountName']", IsRequired = true, InputType = "text", TabIndex = 3 },
            new PersonnelField { FieldName = "İBAN", DisplayName = "İBAN", Selector = "input[name='BankAccountCEViewModel.AccountNumber']", IsRequired = true, InputType = "text", MaxLength = 26, TabIndex = 3 }
        };

        public PersonnelWindow()
        {
            InitializeComponent();
            LoadSettings();

            // Personel ekleme için form oluştur
            InitializePersonnelAddTab();

            // Geçmiş verilerini yükle
            LoadPersonnelHistory();

            // Saat güncelleme timer'ı başlat
            StartClockTimer();

            // İşlemi başlat butonunu başlangıçta disable et (dosya seçilmeden)
            btnFooterStart.IsEnabled = false;

            // Config boşsa Ayarlar tab'ını açtır
            if (IsLoginConfigMissing())
            {
                // Bilgilendir
                MessageBox.Show("Login bilgileri eksik. Lütfen Ayarlar sekmesinden Firma Kodu, Kullanıcı ID, Şifre ve gerekirse TOTP Secret girin.",
                    "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);

                // Ayarlar tab'ına geç
                // Tab sırası: 0=Ana İşlemler, 1=Personel Ekle, 2=Ayarlar
                MainTab.SelectedIndex = 2;
            }

            // Closing event handler ekle - pencere kapandığında SelectionWindow'u göster
            this.Closing += PersonnelWindow_Closing;
        }

        private void PersonnelWindow_Closing(object sender, CancelEventArgs e)
        {
            try
            {
                // Temizlik işlemlerini yap
                if (_page != null)
                {
                    try
                    {
                        _page.CloseAsync().Wait(2000);
                    }
                    catch { /* Sessizce geç */ }
                }

                if (_browser != null)
                {
                    try
                    {
                        _browser.CloseAsync().Wait(2000);
                    }
                    catch { /* Sessizce geç */ }
                }

                // Mevcut SelectionWindow'u bul ve göster
                var selectionWindow = Application.Current.Windows.OfType<SelectionWindow>().FirstOrDefault();
                if (selectionWindow != null)
                {
                    selectionWindow.Show();
                    selectionWindow.WindowState = WindowState.Maximized;
                    selectionWindow.Activate();
                }
                else
                {
                    // Eğer bulunamazsa yeni oluştur
                    selectionWindow = new SelectionWindow();
                    selectionWindow.Show();
                }
            }
            catch (Exception ex)
            {
                // Hata olsa bile devam et
                Log($"Pencere kapanırken hata: {ex.Message}");
            }
        }

        private bool IsLoginConfigMissing()
        {
            try
            {
                var p = _config?.Personnel;
                if (p == null) return true;
                return string.IsNullOrWhiteSpace(p.FirmaKodu)
                    || string.IsNullOrWhiteSpace(p.KullaniciId)
                    || string.IsNullOrWhiteSpace(p.Sifre);
            }
            catch
            {
                return true;
            }
        }

        private void PersonnelAddCard_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // Excel dosyası seçimi
                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Personel Excel Dosyası Seçin",
                    Filter = "Excel Files (*.xlsx)|*.xlsx|All Files (*.*)|*.*",
                    Multiselect = false
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    string selectedFilePath = openFileDialog.FileName;

                    // Dosya varlığını kontrol et
                    if (!System.IO.File.Exists(selectedFilePath))
                    {
                        MessageBox.Show("Seçilen dosya bulunamadı!", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // Excel dosyasındaki personel sayısını hesapla
                    int personnelCount = GetPersonnelCountFromExcel(selectedFilePath);
                    if (personnelCount == 0)
                    {
                        MessageBox.Show("Excel dosyasında personel verisi bulunamadı!", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    // Personel sayısı modal'ını göster
                    var message = $"{System.IO.Path.GetFileName(selectedFilePath)} dosyasında {personnelCount} personel bulundu.\n\nPersonel Ekle tab'ına geçmek istiyor musunuz?";
                    var messageResult = MessageBox.Show(message, "Personel Sayısı Onayı", MessageBoxButton.YesNo, MessageBoxImage.Question);

                    Log($"MessageBox sonucu: {messageResult}");

                    if (messageResult == MessageBoxResult.Yes)
                    {
                        // Personel şablonu seçildi - Personel Ekle tab'ına geç
                        Log("📋 Personel şablonu onaylandı, Personel Ekle tab'ına geçiliyor...");

                        // Tab değişikliği yap
                        Log($"MainTab mevcut: {MainTab != null}, Mevcut tab index: {MainTab?.SelectedIndex}");
                        MainTab.SelectedIndex = 1; // Personel Ekle tab'ı
                        Log($"Tab değiştirildi, yeni tab index: {MainTab?.SelectedIndex}");

                        // Excel verisini yükle
                        LoadExcelData(selectedFilePath);
                    }
                    else
                    {
                        Log("📋 Personel sayısı onaylandı, iptal edildi.");
                    }
                }
                // Dosya seçilmediğinde hiçbir şey yapma - tab geçişi yapma
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Personel ekleme işlemi başlatılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MainTab_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                // Ana işlemler tab'ına geçildiğinde sözleşmeli personel modunu reset et (sadece Excel verisi yoksa)
                if (MainTab.SelectedIndex == 0) // Ana İşlemler tab'ı
                {
                    if (_excelData == null || _excelData.Count == 0) // Sadece Excel verisi yüklenmemişse reset et
                {
                    _isSözleşmeliPersonelMode = false;
                    Log($"🔄 Ana işlemler tab'ına geçildi, sözleşmeli personel modu reset edildi. _isSözleşmeliPersonelMode = {_isSözleşmeliPersonelMode}");
                    UpdatePersonnelAddTabHeader();
                    }
                    else
                    {
                        Log($"🔄 Ana işlemler tab'ına geçildi, Excel verisi mevcut olduğu için sözleşmeli personel modu korunuyor. _isSözleşmeliPersonelMode = {_isSözleşmeliPersonelMode}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Tab değişim hatası: {ex.Message}");
            }
        }

        private void JobEntryCard_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // Sözleşmeli personel modu aktif et
                _isSözleşmeliPersonelMode = true;
                Log($"📝 Sözleşmeli personel modu aktif edildi. _isSözleşmeliPersonelMode = {_isSözleşmeliPersonelMode}");

                // Personel Ekle tab'ına geç (ama SelectionChanged'i tetiklemeyecek şekilde)
                // Önce event'i geçici olarak kaldır
                MainTab.SelectionChanged -= MainTab_SelectionChanged;

                MainTab.SelectedIndex = 1; // Personel Ekle tab'ı

                // Event'i geri ekle
                MainTab.SelectionChanged += MainTab_SelectionChanged;

                // Başlığı güncelle
                UpdatePersonnelAddTabHeader();

                // Sözleşmeli personel için dosya seçme dialogu aç
                OpenSozPersonelExcelDialog();

                Log("📝 Sözleşmeli personel kayıt modu aktif edildi - dialog açıldı");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Sözleşmeli personel modu açılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenSozPersonelExcelDialog()
        {
            try
            {
                var openDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Sözleşmeli Personel Excel Dosyası Seç",
                    Filter = "Excel Files (*.xlsx)|*.xlsx|All Files (*.*)|*.*",
                    DefaultExt = ".xlsx"
                };

                if (openDialog.ShowDialog() == true)
                {
                    string selectedFilePath = openDialog.FileName;

                    // Dosya varlığını kontrol et
                    if (!System.IO.File.Exists(selectedFilePath))
                    {
                        MessageBox.Show("Seçilen dosya bulunamadı!", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // Excel dosyasındaki personel sayısını hesapla
                    int personnelCount = GetPersonnelCountFromExcel(selectedFilePath);
                    if (personnelCount == 0)
                    {
                        MessageBox.Show("Excel dosyasında personel verisi bulunamadı!", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    // Personel sayısı modal'ını göster
                    var message = $"{System.IO.Path.GetFileName(selectedFilePath)} dosyasında {personnelCount} sözleşmeli personel bulundu.\n\nPersonel Ekle tab'ına geçmek istiyor musunuz?";
                    var messageResult = MessageBox.Show(message, "Sözleşmeli Personel Sayısı Onayı", MessageBoxButton.YesNo, MessageBoxImage.Question);

                    Log($"MessageBox sonucu: {messageResult}");

                    if (messageResult == MessageBoxResult.Yes)
                    {
                        // Sözleşmeli personel şablonu seçildi - Personel Ekle tab'ına geç
                        Log("📋 Sözleşmeli personel şablonu onaylandı, Personel Ekle tab'ına geçiliyor...");

                        // Tab değişikliği yap
                        Log($"MainTab mevcut: {MainTab != null}, Mevcut tab index: {MainTab?.SelectedIndex}");
                        MainTab.SelectedIndex = 1; // Personel Ekle tab'ı
                        Log($"Tab değiştirildi, yeni tab index: {MainTab?.SelectedIndex}");

                        // Excel verisini yükle
                        LoadExcelData(selectedFilePath);
                    }
                    else
                    {
                        Log("📋 Sözleşmeli personel sayısı onaylandı, iptal edildi.");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Sözleşmeli personel dosya seçme hatası: {ex.Message}");
            }
        }

        private void UpdatePersonnelAddTabHeader()
        {
            try
            {
                // Personel Ekle tab'ının başlığını güncelle
                var tabItem = MainTab.Items[1] as TabItem;
                if (tabItem != null)
                {
                    if (_isSözleşmeliPersonelMode)
                    {
                        tabItem.Header = "📝 Söz. Personel Ekle";
                    }
                    else
                    {
                        tabItem.Header = "👤 Personel Ekle";
                    }
                }

                // Başlık metnini de güncelle
                var titleTextBlock = FindVisualChild<TextBlock>(tabItem, "PersonnelAddTitle");
                if (titleTextBlock != null)
                {
                    if (_isSözleşmeliPersonelMode)
                    {
                        titleTextBlock.Text = "Sözleşmeli Personel Ekle";
                    }
                    else
                    {
                        titleTextBlock.Text = "Personel Ekle";
                    }
                }

                var subtitleTextBlock = FindVisualChild<TextBlock>(tabItem, "PersonnelAddSubtitle");
                if (subtitleTextBlock != null)
                {
                    if (_isSözleşmeliPersonelMode)
                    {
                        subtitleTextBlock.Text = "Sözleşmeli personel bilgilerini sisteme kaydedin";
                    }
                    else
                    {
                        subtitleTextBlock.Text = "Yeni personel bilgilerini sisteme kaydedin";
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Tab başlığı güncelleme hatası: {ex.Message}");
            }
        }

        private T FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T element && element.Name == name)
                {
                    return element;
                }

                var result = FindVisualChild<T>(child, name);
                if (result != null)
                {
                    return result;
                }
            }
            return null;
        }


        private void btnDownloadTemplate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                DownloadFixedTemplate();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Şablon indirme sırasında hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadSettings()
        {
            try
            {
                _config = ConfigService.LoadConfig<PersonnelConfig>("personnel-config.json");

                // Form alanlarını doldur
                txtFirmaKodu.Text = _config.Personnel.FirmaKodu ?? "";
                txtKullaniciId.Text = _config.Personnel.KullaniciId ?? "";
                txtSifre.Password = _config.Personnel.Sifre ?? "";
                TotpSecret = _config.Personnel.TotpSecret ?? "";
                chkHeadlessMode.IsChecked = _config.Browser.HeadlessMode;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ayarlar yüklenirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Validasyon
                if (string.IsNullOrWhiteSpace(txtFirmaKodu.Text))
                {
                    MessageBox.Show("Firma Kodu alanı boş bırakılamaz!", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtFirmaKodu.Focus();
                    return;
                }

                if (string.IsNullOrWhiteSpace(txtKullaniciId.Text))
                {
                    MessageBox.Show("Kullanıcı ID alanı boş bırakılamaz!", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtKullaniciId.Focus();
                    return;
                }

                if (string.IsNullOrWhiteSpace(txtSifre.Password))
                {
                    MessageBox.Show("Şifre alanı boş bırakılamaz!", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtSifre.Focus();
                    return;
                }

                // Config'i güncelle
                _config.Personnel.FirmaKodu = txtFirmaKodu.Text.Trim();
                _config.Personnel.KullaniciId = txtKullaniciId.Text.Trim();
                _config.Personnel.Sifre = txtSifre.Password.Trim();
                _config.Personnel.TotpSecret = TotpSecret.Trim();
                _config.Browser.HeadlessMode = chkHeadlessMode.IsChecked ?? false;

                // Kaydet
                ConfigService.SaveConfig("personnel-config.json", _config);

                MessageBox.Show("Ayarlar başarıyla kaydedildi!", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ayarlar kaydedilirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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

        private void UpdateStatus(string emoji, string title, string message, string color, string bgColor)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    StatusIcon.Text = emoji;
                    StatusTitle.Text = title;
                    StatusMessage.Text = message;
                });
            }
            catch { }
        }

        private void InitializePersonnelAddTab()
        {
            try
            {
                // Başlangıç durumu
                UpdateStatus("🔄", "Hazırlanıyor", "Personel ekleme sayfası hazırlanıyor...", "#FF9800", "#FFF3E0");

                // Manuel giriş için form oluştur
                GenerateFormFromFields();

                Log("Personel ekleme modu hazır.");
            }
            catch (Exception ex)
            {
                Log($"Personel ekleme modu başlatma hatası: {ex.Message}");
            }
        }

        private void btnResetSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageBox.Show(
                    "Ayarları sıfırlamak istediğinizden emin misiniz?\n\nBu işlem tüm ayarları varsayılan değerlere döndürecektir.",
                    "Ayarları Sıfırla",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // Config'i varsayılan değerlere sıfırla
                    _config.Personnel.FirmaKodu = "";
                    _config.Personnel.KullaniciId = "";
                    _config.Personnel.Sifre = "";
                    _config.Personnel.TotpSecret = "";

                    // Form alanlarını temizle
                    txtFirmaKodu.Text = "";
                    txtKullaniciId.Text = "";
                    txtSifre.Password = "";
                    TotpSecret = "";

                    // Kaydet
                    ConfigService.SaveConfig("personnel-config.json", _config);

                    MessageBox.Show("Ayarlar varsayılan değerlere sıfırlandı!", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ayarlar sıfırlanırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Personel ekleme için event handler'lar
        private async void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Log("İptal işlemi başlatılıyor...");
                // İşlemi iptal et ve tarayıcıyı kapat
                await CleanupBrowserAsync();
                UpdateStatus("⚠️", "İptal Edildi", "İşlem kullanıcı tarafından iptal edildi", "#FF9800", "#FFF3E0");
                Log("İşlem başarıyla iptal edildi.");
            }
            catch (Exception ex)
            {
                Log($"İptal işlemi hatası: {ex.Message}");
                UpdateStatus("❌", "İptal Hatası", $"İptal sırasında hata: {ex.Message}", "#F44336", "#FFEBEE");
            }
        }


        private async void StartProcessButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // İşlem başlatılınca log'ların görüldüğü tab'a geç
                MainTab.SelectedIndex = 1; // Personel Ekle tab'ı (log'lar burada)

                // ÇALIŞTIRMADAN ÖNCE: Login config kontrolü
                if (_config == null)
                {
                    _config = ConfigService.LoadConfig<PersonnelConfig>("personnel-config.json");
                }

                // Her iki mod için de ana personel config'indeki login bilgilerini kontrol et
                // (Sözleşmeli personel modu farklı olsa da tek config var)
                if (_config?.Personnel == null || string.IsNullOrWhiteSpace(_config.Personnel.FirmaKodu) || string.IsNullOrWhiteSpace(_config.Personnel.KullaniciId) || string.IsNullOrWhiteSpace(_config.Personnel.Sifre))
                {
                    UpdateStatus("⚠️", "Ayar Gerekli", "Lütfen Ayarlar sekmesinden Personel için Firma Kodu, Kullanıcı ID ve Şifre girin.", "#FF9800", "#FFF3E0");
                    MessageBox.Show("Login bilgileri eksik. Lütfen Personel İşlemleri ekranındaki Ayarlar sekmesinden Personel için Firma Kodu, Kullanıcı ID ve Şifre girin.", "Ayar Eksik", MessageBoxButton.OK, MessageBoxImage.Warning);
                    MainTab.SelectedIndex = 2; // Ayarlar tab'ına geç
                    return;
                }

                Dictionary<string, string> formData;

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
            }
        }

        // Personel form işlemleri - FormPanel kaldırıldı, artık kullanılmıyor
        private void GenerateFormFromFields()
        {
            // FormPanel kaldırıldı - artık form alanları gösterilmiyor
            _formFields.Clear();
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

        // Browser işlemleri
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
                    SlowMo = _config.Browser.SlowMo,
                    Args = new[] { "--start-maximized" }
                });

                _page = await _browser.NewPageAsync();

                // Mod'a göre URL'e git - login ekranı gelecek
                var createUrl = _isSözleşmeliPersonelMode ? _config.SozPersonel.CreateUrl : _config.Personnel.CreateUrl;
                UpdateStatus("🔄", "İşleniyor", "Siteye bağlanılıyor...", "#FF9800", "#FFF3E0");
                Log($"Tek kayıt işlemi için URL: {createUrl}, Mod: {_isSözleşmeliPersonelMode}, SozPersonel.CreateUrl: {_config.SozPersonel.CreateUrl}, Personnel.CreateUrl: {_config.Personnel.CreateUrl}");
                await _page.GotoAsync(createUrl);

                // Login işlemi
                Log("Login sayfasına gidiliyor...");
                if (!await PerformLoginAsync())
                {
                    throw new Exception("Giriş yapılamadı");
                }
                Log("Login başarılı, personel ekleme sayfasına yönlendiriliyor...");

                // Login sonrası aynı sayfaya tekrar git (artık giriş yapmış olacağız)
                UpdateStatus("🔄", "İşleniyor", "Personel ekleme sayfası yükleniyor...", "#FF9800", "#FFF3E0");
                await _page.GotoAsync(createUrl);

                // Formu doldur
                UpdateStatus("🔄", "İşleniyor", "Form dolduruluyor...", "#FF9800", "#FFF3E0");
                await FillPersonnelFormAsync(formData);

                // Kaydet butonuna bas
                UpdateStatus("🔄", "İşleniyor", "Kaydediliyor...", "#FF9800", "#FFF3E0");
                await _page.ClickAsync("button.btn-outline-primary:has-text('KAYDET')");
                await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

                UpdateStatus("✅", "Başarılı", "Personel başarıyla eklendi!", "#4CAF50", "#E8F5E8");
                MessageBox.Show("Personel başarıyla eklendi!", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);

                // İşlem tamamlandığında Durdur butonunu gizle

                // Tek kayıt için tarayıcıyı açık bırak
                UpdateStatus("ℹ️", "Bilgi", "Tarayıcı açık bırakıldı. Manuel olarak kapatabilirsiniz.", "#2196F3", "#E3F2FD");
                Log("🔄 Tek kayıt tamamlandı - tarayıcı açık bırakılıyor");

                // Browser referanslarını temizle ama kapatmadan
                _page = null;
                _browser = null;
                Log("🔄 Browser referansları temizlendi (ama kapatılmadı)");
            }
            catch (Exception ex)
            {
                throw new Exception($"Personel ekleme hatası: {ex.Message}");
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
                    SlowMo = _config.Browser.SlowMo,
                    Args = new[] { "--start-maximized" }
                });

                // İlk kayıt için login işlemini yap
                _page = await _browser.NewPageAsync();
                var createUrl = _isSözleşmeliPersonelMode ? _config.SozPersonel.CreateUrl : _config.Personnel.CreateUrl;

                Log($"İlk kayıt için login sayfasına gidiliyor... URL: {createUrl}");
                await _page.GotoAsync(createUrl);
                if (!await PerformLoginAsync())
                {
                    Log("İlk kayıt için login başarısız - işlem durduruldu");
                    UpdateStatus("❌", "Hata", "Login başarısız - işlem durduruldu", "#F44336", "#FFCDD2");
                    return;
                }
                Log("İlk kayıt için login başarılı - aynı oturum kullanılacak");

                int successCount = 0;
                int failCount = 0;

                for (int i = 0; i < _excelData.Count; i++)
                {
                    var record = _excelData[i];
                    UpdateStatus("🔄", "İşleniyor", $"{i + 1}/{_excelData.Count} kayıt işleniyor...", "#FF9800", "#FFF3E0");

                    try
                    {
                        // İlk kayıt hariç diğer kayıtlar için yeni sayfaya git (aynı browser, aynı oturum)
                        if (i > 0)
                        {
                            Log($"{i + 1}. kayıt için aynı oturum ile devam ediliyor...");
                            await _page.GotoAsync(createUrl);
                            await Task.Delay(1000); // Sayfa yüklenmesi için bekle
                        }

                        // Formu doldur
                        await FillPersonnelFormAsync(record);

                        // Kaydet butonuna bas
                        await _page.ClickAsync("button.btn-outline-primary:has-text('KAYDET')");

                        // 750ms bekle ve hemen URL'e git (sayfa yenileme için)
                        await Task.Delay(750);
                        await _page.GotoAsync(createUrl);
                        await Task.Delay(500); // Sayfa yüklenmesi için kısa bekleme

                        successCount++;

                        Log($"{i + 1}. kayıt başarıyla kaydedildi");

                        // Geçmişe başarılı kaydı ekle
                        AddToHistory(record, "Başarılı");
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        Log($"{i + 1}. kayıt hatası: {ex.Message}");

                        // Geçmişe başarısız kaydı ekle
                        try
                        {
                            var failedRecord = new PersonnelHistoryRecord
                            {
                                TCKN = record.GetValueOrDefault("TCKN", ""),
                                FirstName = record.GetValueOrDefault("FirstName", ""),
                                LastName = record.GetValueOrDefault("LastName", ""),
                                Status = "Başarısız",
                                Notes = ex.Message,
                                CreatedAt = DateTime.Now
                            };
                            _personnelHistory.Records.Add(failedRecord);
                            SavePersonnelHistory();
                        }
                        catch (Exception historyEx)
                        {
                            Log($"❌ Başarısız kayıt geçmişe eklenirken hata: {historyEx.Message}");
                        }

                        // Hata logla ama devam et
                    }
                }

                UpdateStatus("✅", "Tamamlandı", $"{successCount} başarılı, {failCount} başarısız", "#4CAF50", "#E8F5E8");
                MessageBox.Show($"{successCount} kayıt başarıyla eklendi!\n{failCount} kayıt başarısız oldu.", "İşlem Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);

                // İşlem tamamlandığında Durdur butonunu gizle
            }
            catch (Exception ex)
            {
                throw new Exception($"Çoklu işlem hatası: {ex.Message}");
            }
            finally
            {
                // Tüm kayıtlar işlendikten sonra tarayıcıyı kapat
                Log("🔄 Tüm kayıtlar işlendikten sonra tarayıcı kapatılıyor");
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

                // Config'in doğru yüklendiğini kontrol et
                Log($"✅ Config yüklendi. SozPersonel.CreateUrl: '{_config?.SozPersonel?.CreateUrl ?? "NULL"}'");
                Log($"🔍 Personnel config - FirmaKodu: '{_config?.Personnel?.FirmaKodu ?? "NULL"}', KullaniciId: '{_config?.Personnel?.KullaniciId ?? "NULL"}', Sifre: '{(!string.IsNullOrEmpty(_config?.Personnel?.Sifre) ? "DOLU" : "BOŞ")}'");

                // Config null kontrolü
                if (_config == null)
                {
                    Log("❌ KRİTİK: _config null! Config yüklenemedi!");
                    throw new Exception("Config yüklenemedi - _config null");
                }
                if (_config.Personnel == null)
                {
                    Log("❌ KRİTİK: _config.Personnel null!");
                    throw new Exception("Personnel config null");
                }
                if (_config.SozPersonel == null)
                {
                    Log("❌ KRİTİK: _config.SozPersonel null!");
                    throw new Exception("SozPersonel config null");
                }

                // Her iki mod için de ana personel config'indeki login bilgilerini kullan
                // (Sözleşmeli personel modu farklı olsa da tek config var)
                var firmaKodu = _config.Personnel.FirmaKodu?.Trim();
                var kullaniciId = _config.Personnel.KullaniciId?.Trim();
                var sifre = _config.Personnel.Sifre?.Trim();
                var totpSecret = _config.Personnel.TotpSecret?.Trim();

                // Eksik login bilgilerini kontrol et ve detaylı logla
                var missingFields = new List<string>();
                if (string.IsNullOrWhiteSpace(firmaKodu)) missingFields.Add("Firma Kodu");
                if (string.IsNullOrWhiteSpace(kullaniciId)) missingFields.Add("Kullanıcı ID");
                if (string.IsNullOrWhiteSpace(sifre)) missingFields.Add("Şifre");

                if (missingFields.Any())
                {
                    var errorMsg = $"❌ Login bilgileri eksik! Aşağıdaki alanları doldurun: {string.Join(", ", missingFields)}\n" +
                                   $"💡 Ayarlar sekmesinden personel ayarlarını kontrol edin ve gerekli bilgileri girin.";
                    Log(errorMsg);
                    throw new Exception($"Login bilgileri eksik: {string.Join(", ", missingFields)}");
                }

                // TOTP secret kontrolü (uyarı ver ama devam et)
                if (string.IsNullOrWhiteSpace(totpSecret))
                {
                    Log("⚠️ TOTP Secret eksik! 2FA kodu otomatik oluşturulmayacak, manuel girmeniz gerekebilir.");
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

        /// <summary>
        /// Sözleşmeli personel için özel alanları doldurur (IsValid ve Address)
        /// </summary>
        private async Task FillSozPersonelSpecificFieldsAsync(Dictionary<string, string> data)
        {
            try
            {
                Log("🔧 Sözleşmeli personel özel alanları dolduruluyor...");

                // 1. IsValid (Geçerli mi?) - Her zaman "Evet" seç veya Excel'den oku
                try
                {
                    string isValidValue = "True"; // Default: Evet

                    // Excel'den "GEÇERLİ Mİ?" alanını kontrol et
                    if (data.ContainsKey("GEÇERLİ Mİ?") && !string.IsNullOrWhiteSpace(data["GEÇERLİ Mİ?"]))
                    {
                        var excelValue = data["GEÇERLİ Mİ?"].Trim();
                        if (excelValue.ToLower() == "hayır" || excelValue.ToLower() == "false")
                        {
                            isValidValue = "False";
                            Log($"📋 IsValid Excel'den 'Hayır' olarak ayarlandı");
                        }
                        else
                        {
                            Log($"📋 IsValid Excel'den 'Evet' olarak ayarlandı");
                        }
                    }
                    else
                    {
                        Log($"📋 IsValid default 'Evet' olarak ayarlandı");
                    }

                    await _page.SelectOptionAsync("#IsValid", isValidValue, new PageSelectOptionOptions { Timeout = 3000 });
                    Log($"✅ IsValid alanı '{(isValidValue == "True" ? "Evet" : "Hayır")}' olarak seçildi");
                }
                catch (Exception ex)
                {
                    Log($"⚠️ IsValid alanı seçilemedi: {ex.Message}");
                }

                // 2. Address (Açık Adres) - Excel'den al veya şehir ismini kullan
                try
                {
                    string addressValue = "";

                    // Excel'den AÇIK ADRES alanını kontrol et
                    if (data.ContainsKey("AÇIK ADRES") && !string.IsNullOrWhiteSpace(data["AÇIK ADRES"]))
                    {
                        addressValue = data["AÇIK ADRES"];
                        Log($"📋 Address Excel'den alındı: '{addressValue}'");
                    }
                    else if (data.ContainsKey("Address") && !string.IsNullOrWhiteSpace(data["Address"]))
                    {
                        addressValue = data["Address"];
                        Log($"📋 Address Excel'den alındı: '{addressValue}'");
                    }
                    else
                    {
                        // Excel'de yoksa şehir+ilçe bilgisini birleştir
                        string city = "";
                        string district = "";

                        if (data.ContainsKey("İL") && !string.IsNullOrWhiteSpace(data["İL"]))
                        {
                            city = data["İL"];
                        }
                        if (data.ContainsKey("İLÇE") && !string.IsNullOrWhiteSpace(data["İLÇE"]))
                        {
                            district = data["İLÇE"];
                        }

                        if (!string.IsNullOrWhiteSpace(city) && !string.IsNullOrWhiteSpace(district))
                        {
                            addressValue = $"{district} Mah. {city}";
                            Log($"📋 Address şehir+ilçe olarak oluşturuldu: '{addressValue}'");
                        }
                        else if (!string.IsNullOrWhiteSpace(city))
                        {
                            addressValue = city;
                            Log($"📋 Address şehir olarak ayarlandı: '{addressValue}'");
                        }
                        else
                        {
                            addressValue = "İstanbul"; // Default şehir
                            Log($"📋 Address default şehir olarak ayarlandı: '{addressValue}'");
                        }
                    }

                    // Address alanını doldur
                    await _page.FillAsync("#Address", addressValue, new PageFillOptions { Timeout = 3000 });
                    Log($"✅ Address alanı dolduruldu: '{addressValue}'");
                }
                catch (Exception ex)
                {
                    Log($"⚠️ Address alanı doldurulamadı: {ex.Message}");
                }

                Log("✅ Sözleşmeli personel özel alanları tamamlandı");
            }
            catch (Exception ex)
            {
                Log($"❌ Sözleşmeli personel özel alanlar hatası: {ex.Message}");
            }
        }

        private async Task FillPersonnelFormAsync(Dictionary<string, string> data)
        {
            // Sözleşmeli personel modu için özel alanları işle
            if (_isSözleşmeliPersonelMode)
            {
                await FillSozPersonelSpecificFieldsAsync(data);
            }

            int currentTab = 1;
            bool tab1Backfilled = false;

            // İlk doldurma sırasında tüm alanları backfill gibi doldur
            Log("🚀 İlk doldurma başlıyor - tüm alanlar backfill gibi doldurulacak");

            foreach (var field in _personnelFields.OrderBy(f => f.TabIndex))
            {
                // Tab değişimi gerekiyorsa
                if (field.TabIndex != currentTab)
                {
                    // Tab1'den çıkmadan hemen önce: Tab1 alanlarını son kez kontrol et ve boşları doldur
                    if (currentTab == 1 && !tab1Backfilled)
                    {
                        await BackfillTab1IfNeededAsync(data);
                        tab1Backfilled = true;
                    }

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

                // Bazı Tab 2 alanlarını atla (Engel Açıklaması, Özel Durumlar, Kart Id, Kart No)
                string[] skipFields = { "DisabilityDescription", "SpecialConditions", "CardId", "CardNo" };
                if (skipFields.Contains(field.FieldName))
                {
                    Log($"⏭️ Atlanıyor: {field.DisplayName} (istenmeyen alan)");
                    continue;
                }

                // İlk doldurma sırasında tüm alanları işle (backfill gibi)
                bool shouldProcess = true; // Her zaman işle
                Log($"🔍 Field kontrolü: {field.DisplayName} - HasKey: {data.ContainsKey(field.FieldName)}, TabIndex: {field.TabIndex}, ShouldProcess: {shouldProcess}");

                if (shouldProcess)
                {
                    string value = data.ContainsKey(field.FieldName) ? data[field.FieldName] : "";
                    Log($"📋 Value alındı: '{value}' (uzunluk: {value.Length})");

                    // DefaultValue varsa kullan
                    if (string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(field.DefaultValue))
                    {
                        value = field.DefaultValue;
                        Log($"📋 DefaultValue kullanıldı: '{value}'");
                    }

                    bool shouldFill = !string.IsNullOrEmpty(value) || field.TabIndex == 2;
                    Log($"📋 ShouldFill: {shouldFill} (value not empty: {!string.IsNullOrEmpty(value)}, tab 2: {field.TabIndex == 2})");

                    if (shouldFill)
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(value))
                            {
                                Log($"{field.DisplayName}: {value} (Tab {field.TabIndex})");
                            }
                            else
                            {
                                Log($"{field.DisplayName}: [BOŞ - Tab 2 testi için işleniyor] (Tab {field.TabIndex})");
                            }

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
                                   Log($"🔍 {field.DisplayName} alanı için selector deneniyor: {selector}");

                            switch (field.InputType.ToLower())
                                   {
                                       case "select":
                                           // Select2 için direkt UI üzerinden seçim yap (hızlı)
                                           if (field.FieldName == "EducationLevel")
                                           {
                                               try
                                               {
                                                   // Select2 container'ı aç ve yaz
                                                   await _page.ClickAsync("span.select2-selection.select2-selection--single[aria-labelledby='select2-EducationLevel-container']", new PageClickOptions { Timeout = 2000 });
                                                   await Task.Delay(100);
                                                   await _page.Keyboard.TypeAsync(value, new KeyboardTypeOptions { Delay = 50 });
                                                   await _page.Keyboard.PressAsync("Enter");
                                                   fieldFilled = true;
                                                   Log($"✅ {field.DisplayName} alanı Select2 UI ile seçildi => '{value}'");
                                               }
                                               catch (Exception exUi)
                                               {
                                                   Log($"❌ {field.DisplayName} Select2 UI seçimi başarısız: {exUi.Message}");
                                               }
                                           }
                                           else
                                           {
                                               // Diğer select'ler (İl, İlçe, vb.) için JS ile akıllı seçim
                                               try
                                               {
                                                   // Önce value ile dene
                                                   try
                                                   {
                                                       await _page.SelectOptionAsync(selector, value, new PageSelectOptionOptions { Timeout = 1000 });
                                                       fieldFilled = true;
                                                       Log($"✅ {field.DisplayName} alanı SELECT (value) ile dolduruldu ({selector}) => '{value}'");
                                                   }
                                                   catch
                                                   {
                                                       // Banka için özel akıllı seçim
                                               Log($"🔍 BANKA: Excel'den gelen değer: '{value}'");
                                               var bankSelectionResult = await _page.EvaluateAsync<string>(@"(sel, val) => {
                                                           const el = document.querySelector(sel);
                                                           if (!el) return 'SELECT_NOT_FOUND';

                                                           const searchVal = (val || '').trim().toLowerCase();

                                                           // Banka isimlerini standardize et
                                                           const bankaMappings = {
                                                               'ziraat': ['ziraat', 'ziraat bankası', 'türkiye ziraat bankası'],
                                                               'halk': ['halk', 'halk bankası'],
                                                               'vakıf': ['vakıf', 'vakıf bank', 'vakıfbank'],
                                                               'garanti': ['garanti', 'garanti bankası', 'garantibank'],
                                                               'akbank': ['akbank', 'ak bank'],
                                                               'yapıkredi': ['yapıkredi', 'yapı kredi', 'yapıkredi bankası'],
                                                               'is': ['iş', 'iş bankası', 'isbank', 'işbank'],
                                                               'teb': ['teb', 'türk ekonomi bankası', 'türk ekonomi'],
                                                               'deniz': ['deniz', 'deniz bank', 'denizbank'],
                                                               'ing': ['ing', 'ing bank', 'ingbank'],
                                                               'qnb': ['qnb', 'qnb finansbank', 'finansbank', 'qnb finans'],
                                                               'diğer': ['diğer', 'other', 'başka']
                                                           };

                                                           // 1. Banka mapping ile eşleştirme
                                                           let option = null;
                                                           for (const [bankKey, variations] of Object.entries(bankaMappings)) {
                                                               if (variations.some(v => searchVal.includes(v) || v.includes(searchVal))) {
                                                                   // Mapping'e uyan bankayı bul
                                                                   option = Array.from(el.options).find(o => {
                                                                       const optionText = (o.textContent || '').trim().toLowerCase();
                                                                       return variations.some(v =>
                                                                           optionText.includes(v) ||
                                                                           v.includes(optionText) ||
                                                                           optionText === bankKey ||
                                                                           bankKey.includes(optionText)
                                                                       );
                                                                   });
                                                                   if (option) break;
                                                               }
                                                           }

                                                           // 2. Eğer mapping ile bulunmadıysa, normalize edilmiş arama
                                                           if (!option) {
                                                               const normalize = str => str.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase();
                                                               const normalizedSearch = normalize(searchVal);

                                                               option = Array.from(el.options).find(o =>
                                                                   normalize((o.textContent || '').trim()).includes(normalizedSearch) ||
                                                                   normalizedSearch.includes(normalize((o.textContent || '').trim()))
                                                               );
                                                           }

                                                           // 3. Hala bulunmadıysa, basit contains
                                                           if (!option) {
                                                               option = Array.from(el.options).find(o =>
                                                                   (o.textContent || '').trim().toLowerCase().includes(searchVal) ||
                                                                   searchVal.includes((o.textContent || '').trim().toLowerCase())
                                                               );
                                                           }

                                                           // 4. Son çare: İlk option'u seç
                                                           if (!option && el.options.length > 0) {
                                                               option = el.options[0];
                                                           }

                                                           if (option) {
                                                               el.value = option.value;
                                                               el.dispatchEvent(new Event('change'));
                                                               return `SUCCESS: ${(option.textContent || '').trim()} (${option.value})`;
                                                           }
                                                           return 'FAILED: No matching option found';
                                                       }", new object[] { selector, value });

                                                       if (bankSelectionResult.StartsWith("SUCCESS"))
                                                       {
                                                           fieldFilled = true;
                                                           Log($"✅ {field.DisplayName} alanı SELECT (text match) ile dolduruldu ({selector}) => '{value}'");
                                                           Log($"🎯 BANKA Seçimi: {bankSelectionResult}");
                                                       }
                                                       else
                                                       {
                                                           Log($"❌ BANKA Seçimi başarısız: {bankSelectionResult}");
                                                           Log($"❌ {field.DisplayName} SELECT'te '{value}' bulunamadı ({selector})");
                                                       }
                                                   }
                                               }
                                               catch (Exception exSel)
                                               {
                                                   Log($"❌ {field.DisplayName} SELECT seçimi başarısız ({selector}): {exSel.Message}");
                                               }
                                           }
                                           break;

                                       case "checkbox":
                                           var checkboxValue = value == "Evet" || value == "1" || value.ToLower() == "true";
                                           if (checkboxValue)
                                           {
                                               await _page.CheckAsync(selector);
                                               Log($"✅ {field.DisplayName} alanı CHECKBOX ile işaretlendi ({selector})");
                                           }
                                           else
                                           {
                                               await _page.UncheckAsync(selector);
                                               Log($"✅ {field.DisplayName} alanı CHECKBOX ile işaret kaldırıldı ({selector})");
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
                                           Log($"✅ {field.DisplayName} alanı DUAL (checkbox+radio) ile dolduruldu ({selector})");
                                           break;

                                case "date":
                                    // İstek: maskeli alana elle yazım gibi. Öncelik ddMMyyyy.
                                    var candidates = new List<string>();
                                    var digitsOnly = new string((value ?? "").Where(char.IsDigit).ToArray());
                                    if (digitsOnly.Length == 8) candidates.Add(digitsOnly);
                                    if (DateTime.TryParse(value, out DateTime dateValueTmp))
                                    {
                                        candidates.Add(dateValueTmp.ToString("ddMMyyyy"));
                                        candidates.Add(dateValueTmp.ToString("yyyy-MM-dd"));
                                        candidates.Add(dateValueTmp.ToString("dd.MM.yyyy"));
                                    }
                                    if (!string.IsNullOrWhiteSpace(value)) candidates.Add(value);

                                    bool dateSet = false;
                                    foreach (var v in candidates.Distinct())
                                    {
                                        // 1) Klavye ile yaz (Ctrl+A, Delete, Type)
                                        try
                                        {
                                            await _page.ClickAsync(selector);
                                            await _page.Keyboard.PressAsync("Control+A");
                                            await _page.Keyboard.PressAsync("Delete");
                                            await _page.Keyboard.TypeAsync(v, new KeyboardTypeOptions { Delay = 50 });
                                            // Değeri doğrula
                                            var typed = await _page.EvaluateAsync<string>(@"sel => {
                                                const el = document.querySelector(sel);
                                                return el ? el.value || '' : '';
                                            }", selector);
                                            if (!string.IsNullOrEmpty(typed))
                                            {
                                                Log($"✅ {field.DisplayName} alanı DATE (Type) yazıldı ({selector}) => '{v}'");
                                                dateSet = true;
                                                break;
                                            }
                                        }
                                        catch { /* klavye yazımı başarısız olabilir */ }

                                        // 2) Fill ile dene
                                        try
                                        {
                                            await _page.FillAsync(selector, v);
                                            var filled = await _page.EvaluateAsync<string>(@"sel => {
                                                const el = document.querySelector(sel);
                                                return el ? el.value || '' : '';
                                            }", selector);
                                            if (!string.IsNullOrEmpty(filled))
                                            {
                                                Log($"✅ {field.DisplayName} alanı DATE (Fill) yazıldı ({selector}) => '{v}'");
                                                dateSet = true;
                                                break;
                                            }
                                        }
                                        catch { }

                                        // 3) JS ile set et ve event tetikle
                                        try
                                        {
                                            await _page.EvaluateAsync(@"(sel, val) => {
                                                const el = document.querySelector(sel);
                                                if (!el) return;
                                                el.value = val;
                                                el.dispatchEvent(new Event('input', { bubbles: true }));
                                                el.dispatchEvent(new Event('change', { bubbles: true }));
                                            }", new object[] { selector, v });
                                            var jsSet = await _page.EvaluateAsync<string>(@"sel => {
                                                const el = document.querySelector(sel);
                                                return el ? el.value || '' : '';
                                            }", selector);
                                            if (!string.IsNullOrEmpty(jsSet))
                                            {
                                                Log($"✅ {field.DisplayName} alanı DATE (JS) set edildi ({selector}) => '{v}'");
                                                dateSet = true;
                                                break;
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            Log($"❌ {field.DisplayName} için tarih yazma denemesi başarısız: {ex.Message}");
                                        }
                                    }

                                    if (!dateSet)
                                    {
                                        throw new Exception("Tarih yazılamadı");
                                    }

                                    fieldFilled = true;
                                    break;

                                case "subcontractors":
                                    // Alt yüklenici listesinde isim/numara eşleşmesine göre hem checkbox hem radio button seç
                                    try
                                    {
                                        // value: virgülle ayrılmış isimler olabilir
                                        var wanted = value.Split(',').Select(v => v.Trim()).Where(v => v.Length > 0).ToList();
                                        if (wanted.Count == 0)
                                        {
                                            // "Varsayılan" ya da hepsi için inputları es geç
                                            fieldFilled = true;
                                            Log($"ℹ️ Alt Yüklenici için değer verilmedi, atlandı");
                                            break;
                                        }

                                        foreach (var w in wanted)
                                        {
                                            // Span içindeki metne göre ara (<span class="">İsim (telefon)</span>)
                                            var spans = await _page.QuerySelectorAllAsync($"{field.Selector} span");
                                            IElementHandle span = null;
                                            foreach (var s in spans)
                                            {
                                                var text = await s.TextContentAsync();
                                                if (text.Contains(w))
                                                {
                                                    span = s;
                                                    Log($"🔍 Alt yüklenici bulundu: '{text}' için '{w}'");
                                                    break;
                                                }
                                            }
                                            if (span == null)
                                            {
                                                Log($"⚠️ Alt Yüklenici bulunamadı: {w}");
                                                continue;
                                            }

                                            // JavaScript ile span'ın bulunduğu container'daki input'ları bul ve tıkla
                                            try
                                            {
                                                string jsCode = $@"
                                                    (function(spanText) {{
                                                        const spans = document.querySelectorAll('span');
                                                        for (let span of spans) {{
                                                            if (span.textContent && span.textContent.includes(spanText)) {{
                                                                let element = span;
                                                                while (element && !element.classList.contains('list-group-item')) {{
                                                                    element = element.parentElement;
                                                                }}

                                                                if (element) {{
                                                                    const checkbox = element.querySelector('input[name=""AgcTeamLeaderIds""]');
                                                                    if (checkbox) {{
                                                                        checkbox.click();
                                                                        console.log('Checkbox clicked for:', spanText);
                                                                    }}

                                                                    const radio = element.querySelector('input[name=""DefaultAgcTeamLeaderId""]');
                                                                    if (radio) {{
                                                                        radio.click();
                                                                        console.log('Radio clicked for:', spanText);
                                                                    }}
                                                                }}
                                                                break;
                                                            }}
                                                        }}
                                                    }})";

                                                await _page.EvaluateAsync(jsCode, w);
                                                Log($"✅ Alt yüklenici seçildi: {w}");
                                            }
                                            catch (Exception ex)
                                            {
                                                Log($"❌ Alt yüklenici seçimi hatası: {ex.Message}");
                                            }
                                        }

                                        fieldFilled = true;
                                    }
                                    catch (Exception ex)
                                    {
                                        Log($"❌ Alt Yüklenici seçimi hatası: {ex.Message}");
                                    }
                                    break;

                                       default: // text
                                           // AD/SOYAD için mevcut değeri kontrol et
                                           if (field.FieldName == "FirstName" || field.FieldName == "LastName")
                                           {
                                               try
                                               {
                                                   var currentValue = await _page.GetAttributeAsync(selector, "value", new PageGetAttributeOptions { Timeout = 1000 }) ?? "";
                                                   if (currentValue.Trim() != value.Trim())
                                                   {
                                                       await _page.ClickAsync(selector, new PageClickOptions { Timeout = 1000 });
                                                       await _page.Keyboard.PressAsync("Control+A");
                                                       await _page.Keyboard.TypeAsync(value, new KeyboardTypeOptions { Delay = 30 });
                                                       Log($"✅ {field.DisplayName} alanı KEYBOARD ile yazıldı ({selector}) - Önceki: '{currentValue}', Yeni: '{value}'");
                                                   }
                                                   else
                                                   {
                                                       Log($"✅ {field.DisplayName} alanı zaten doğru değere sahip ({selector}) - Mevcut: '{currentValue}'");
                                                   }
                                                   fieldFilled = true;
                                               }
                                               catch (Exception exText)
                                               {
                                                   Log($"❌ {field.DisplayName} keyboard yazımı başarısız: {exText.Message}");
                                               }
                                           }
                                           else
                                           {
                                               // IBAN alanı için özel işlem - Ctrl+A ile seçip yaz
                                               if (field.DisplayName == "İBAN")
                                               {
                                                   try
                                                   {
                                                       // IBAN'ı normalize et - TR varsa kaldır, boşlukları temizle
                                                       var ibanValue = (value ?? "").Trim();
                                                       ibanValue = ibanValue.Replace(" ", ""); // Boşlukları kaldır

                                                       // Eğer TR ile başlıyorsa, TR'yi kaldır (sistemde zaten var)
                                                       if (ibanValue.ToUpper().StartsWith("TR"))
                                                       {
                                                           ibanValue = ibanValue.Substring(2);
                                                       }

                                                       // Sadece sayısal kısmı al (TR'den sonraki kısım)
                                                       ibanValue = ibanValue.Trim();

                                                       await _page.ClickAsync(selector, new PageClickOptions { Timeout = 1000 });
                                                       await Task.Delay(100);
                                                       await _page.Keyboard.PressAsync("Control+A");
                                                       await Task.Delay(50);
                                                       await _page.Keyboard.TypeAsync(ibanValue, new KeyboardTypeOptions { Delay = 50 });
                                                       Log($"✅ {field.DisplayName} alanı IBAN özel yöntemle dolduruldu ({selector}) => '{ibanValue}' (orijinal: '{value}')");

                                                       // IBAN sisteme işlenmesi için 500ms bekle
                                                       await Task.Delay(500);
                                                       Log($"⏳ IBAN sisteme işlenmesi için 500ms beklendi");

                                                       fieldFilled = true;
                                                   }
                                                   catch (Exception exIban)
                                                   {
                                                       Log($"❌ {field.DisplayName} IBAN özel yöntem başarısız: {exIban.Message}");
                                                   }
                                               }
                                               else
                                               {
                                                   // Diğer text alanlar için hızlı Fill
                                                   try
                                                   {
                                                       await _page.FillAsync(selector, value, new PageFillOptions { Timeout = 2000 });
                                                       Log($"✅ {field.DisplayName} alanı TEXT ile dolduruldu ({selector})");
                                                       fieldFilled = true;
                                                   }
                                                   catch (Exception exFill)
                                                   {
                                                       Log($"❌ {field.DisplayName} Fill başarısız: {exFill.Message}");
                                                   }
                                               }
                                           }
                                           break;
                                   }

                                   if (fieldFilled)
                                   {
                                       break; // Başarılı oldu, diğer selector'ları dene
                                   }
                               }
                               catch (Exception ex)
                               {
                                   Log($"❌ {field.DisplayName} alanı için selector başarısız ({selector}): {ex.Message}");
                                   // Bu selector çalışmadı, devam et
                                   continue;
                               }
                           }

                           if (!fieldFilled)
                           {
                               Log($"❌❌❌ {field.DisplayName} alanı HİÇBİR SELECTOR ile BULUNAMADI - GEÇİLDİ!");
                           }

                           // TCKN girildikten sonra ekstra bekleme (sayfa tepki verebilir)
                           if (field.FieldName == "TCKN")
                           {
                               await Task.Delay(1500); // TCKN sonrası 1.5 saniye bekle
                               Log("TCKN girildi, sayfa tepkisi bekleniyor...");
                           }
                           else if (field.FieldName == "FirstName" || field.FieldName == "LastName")
                           {
                               // AD ve SOYAD için TCKN sonrası ekstra bekleme
                               await Task.Delay(300); // AD/SOYAD arası 300ms bekleme
                           }
                           else
                           {
                               await Task.Delay(200); // Diğer alanlar arası 200ms bekleme
                           }
                        }
                        catch (Exception ex)
                        {
                            // Alan bulunamazsa logla ama devam et
                            Log($"❌ Alan doldurulurken hata ({field.DisplayName}): {ex.Message}");

                            // Banka alanı için ekstra debug
                            if (field.FieldName == "BANKA")
                            {
                                Log($"🔍 BANKA Debug - Value: '{value}', Field Selector: '{field.Selector}', TabIndex: {field.TabIndex}");
                                try
                                {
                                    var selectElement = await _page.QuerySelectorAsync(field.Selector);
                                    if (selectElement == null)
                                    {
                                        Log($"🔍 BANKA Debug - Select element BULUNAMADI! Selector: '{field.Selector}'");
                                        var allSelects = await _page.QuerySelectorAllAsync("select");
                                        Log($"🔍 BANKA Debug - Sayfada toplam {allSelects.Count()} select elementi var");
                                        foreach (var sel in allSelects)
                                        {
                                            var id = await sel.GetAttributeAsync("id") ?? "null";
                                            var name = await sel.GetAttributeAsync("name") ?? "null";
                                            Log($"🔍 BANKA Select found - ID: '{id}', Name: '{name}'");
                                        }
                                    }
                                    else
                                    {
                                        Log($"🔍 BANKA Debug - Select element BULUNDU!");
                                        var options = await selectElement.QuerySelectorAllAsync("option");
                                        Log($"🔍 BANKA Debug - Found {options.Count()} options in select");
                                        foreach (var opt in options.Take(5)) // İlk 5 taneyi göster
                                        {
                                            var text = await opt.TextContentAsync();
                                            var val = await opt.GetAttributeAsync("value");
                                            Log($"🔍 BANKA Option: '{text}' -> '{val}'");
                                        }
                                    }
                                }
                                catch (Exception debugEx)
                                {
                                    Log($"🔍 BANKA Debug error: {debugEx.Message}");
                                }
                            }
                        }
                    }
                }
            }

            // Tab1 sonrası kontrol, eğer tab değişmeden önce yapılmadıysa güvenlik için tekrar çalıştır
            if (!tab1Backfilled)
            {
                await BackfillTab1IfNeededAsync(data);
            }
        }

        // Tab1 alanlarını (özellikle Adı/Soyadı) tab değişiminden HEMEN ÖNCE tekrar kontrol eder ve boşsa doldurur
        private async Task BackfillTab1IfNeededAsync(Dictionary<string, string> data)
        {
            try
            {
                Log("📋 Tab1 çıkışı: boş kalan input'lar için son kontrol yapılıyor...");
                await Task.Delay(300);

                var tab1Fields = _personnelFields.Where(f => f.TabIndex == 1).ToList();
                int totalFields = 0; int emptyFields = 0; int filledFields = 0;

                foreach (var field in tab1Fields)
                {
                    // İlk doldurma sırasında tüm alanları işle (backfill gibi)
                    var value = data.ContainsKey(field.FieldName) ? data[field.FieldName] : "";
                    if (string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(field.DefaultValue)) value = field.DefaultValue;
                    if (string.IsNullOrEmpty(value)) continue;
                    totalFields++;

                    // Alternatif selector'lar
                    string[] selectorsToTry;
                    if (field.Selector.StartsWith("#"))
                    {
                        var fieldName = field.FieldName;
                        var elementType = field.InputType == "select" ? "select" : "input";
                        selectorsToTry = new[] { field.Selector, $"{elementType}[id='{fieldName}']", $"{elementType}[name='{fieldName}']", $"[id='{fieldName}']", $"[name='{fieldName}']" };
                    }
                    else
                    {
                        var fieldName = field.FieldName;
                        selectorsToTry = new[] { field.Selector, $"#{fieldName}", $"input[id='{fieldName}']", $"input[name='{fieldName}']", $"select[id='{fieldName}']", $"select[name='{fieldName}']" };
                    }

                    bool fixedNow = false;
                    foreach (var selector in selectorsToTry)
                    {
                        try
                        {
                            var currentValue = await _page.GetAttributeAsync(selector, "value") ?? "";
                            if (!string.IsNullOrEmpty(currentValue.Trim()))
                            {
                                filledFields++;
                                fixedNow = true;
                                break; // Zaten dolu, başka selector dene
                            }

                            // Select alanları için SelectOptionAsync kullan, diğerleri için FillAsync
                            if (field.InputType == "select")
                            {
                                // Select alanları için click yapmadan doğrudan seç
                                try
                                {
                                    await _page.SelectOptionAsync(selector, value);
                                    Log($"✅ (Tab1 backfill) {field.DisplayName} select ile dolduruldu ({selector})");
                                }
                                catch
                                {
                                    // SelectOptionAsync başarısız olursa FillAsync dene
                                    await _page.FillAsync(selector, value);
                                    Log($"✅ (Tab1 backfill) {field.DisplayName} fill ile dolduruldu ({selector})");
                                }
                            }
                            else
                            {
                                // Diğer alanlar için FillAsync
                                await _page.FillAsync(selector, value);
                                Log($"✅ (Tab1 backfill) {field.DisplayName} dolduruldu ({selector})");
                            }

                            emptyFields++;
                            fixedNow = true;
                            break; // Başarılı oldu, başka selector dene
                        }
                        catch { continue; }
                    }

                    if (!fixedNow)
                    {
                        Log($"⚠️ (Tab1 backfill) {field.DisplayName} için uygun input bulunamadı");
                    }
                }

                Log($"📊 Tab1 backfill sonucu: Toplam {totalFields}, dolu {filledFields}, doldurulan {emptyFields}");
            }
            catch (Exception ex)
            {
                Log($"❌ Tab1 backfill hatası: {ex.Message}");
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

        // Tab isimlerini belirle
        string tabName = "";
        switch (tabIndex)
        {
            case 1: tabName = "Kimlik Bilgileri"; break;
            case 2: tabName = "Ekstra Bilgiler"; break;
            case 3: tabName = "Banka Bilgileri"; break;
        }

        // Tab'a tıkla
        Log($"{tabName} tab'ına geçiliyor...");
        await _page.ClickAsync(tabSelector);
                await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

                // Tab geçişinden sonra form alanlarının yüklenmesini bekle
                await _page.WaitForTimeoutAsync(2000); // 2 saniye bekle

                // Tab'ın aktif olduğunu doğrula
                var activeTab = await _page.QuerySelectorAsync(".nav-tabs .active");
                if (activeTab != null)
                {
                    Log($"✅ {tabName} tab'ına başarıyla geçildi");

                    // Ekstra bekleme - form alanlarının tamamen yüklenmesi için
                    await _page.WaitForTimeoutAsync(1000); // 1 saniye daha bekle
                }
                else
                {
                    Log($"⚠️ {tabName} tab geçişi doğrulanamadı");
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
                Log("🧹 CleanupBrowserAsync çağrıldı - tarayıcı kapatılıyor!");
                if (_page is not null)
                {
                    await _page.CloseAsync();
                    _page = null;
                    Log("🧹 Page kapatıldı");
                }

                if (_browser is not null)
                {
                    await _browser.CloseAsync();
                    _browser = null;
                    Log("🧹 Browser kapatıldı");
                }
            }
            catch (Exception ex)
            {
                Log($"🧹 Cleanup hatası: {ex.Message}");
            }
        }


        private int GetPersonnelCountFromExcel(string excelFilePath)
        {
            try
            {
                if (string.IsNullOrEmpty(excelFilePath) || !System.IO.File.Exists(excelFilePath))
                {
                    return 0;
                }

                // Excel dosyasını oku
                ExcelPackage.License.SetNonCommercialPersonal("WebScraper");
                int personnelCount = 0;

                using (var package = new ExcelPackage(new System.IO.FileInfo(excelFilePath)))
                {
                    var worksheet = package.Workbook.Worksheets[0]; // İlk worksheet'i al
                    if (worksheet == null)
                    {
                        return 0;
                    }

                    // Veri satırlarını say (2. satırdan itibaren)
                    for (int row = 2; row <= worksheet.Dimension.End.Row; row++)
                    {
                        bool hasData = false;
                        for (int col = 1; col <= worksheet.Dimension.End.Column; col++)
                        {
                            var value = worksheet.Cells[row, col].Value?.ToString()?.Trim();
                            if (!string.IsNullOrEmpty(value))
                            {
                                hasData = true;
                                break;
                            }
                        }

                        if (hasData)
                        {
                            personnelCount++;
                        }
                    }
                }

                return personnelCount;
            }
            catch (Exception ex)
            {
                Log($"Excel personel sayısı hesaplanırken hata: {ex.Message}");
                return 0;
            }
        }

        private void LoadExcelData(string excelFilePath)
        {
            try
            {
                _excelFilePath = excelFilePath;
                _useExcelData = true;

                // İşlemi başlat butonunu aktif et
                btnFooterStart.IsEnabled = true;

                if (string.IsNullOrEmpty(_excelFilePath) || !System.IO.File.Exists(_excelFilePath))
                {
                    MessageBox.Show("Excel dosyası bulunamadı!", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Excel sütun isimlerini PersonnelField FieldName'lerine map et
                var columnMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    {"TCKN", "TCKN"},
                    {"AD", "FirstName"},
                    {"SOYAD", "LastName"},
                    {"CİNSİYET", "Gender"},
                    {"MEDENİ DURUMU", "MaritalState"},
                    {"ÇOCUK SAYISI", "ChildrenCount"},
                    {"BABA ADI", "FatherName"},
                    {"ANA ADI", "MotherName"},
                    {"MESLEK", "JobDescription"},
                    {"EMEKLİ Mİ?", "IsRetired"},
                    {"DOĞUM TARİHİ", "BirthDate"},
                    {"TEL NO", "Phone"},
                    {"E-POSTA", "Email"},
                    {"ÖĞRENİM DURUMU", "EducationLevel"},
                    {"ENGELLİ", "IsDisabled"},
                    {"ENGEL AÇIKLAMASI", "DisabilityDescription"},
                    {"ÖZEL DURUMLAR", "SpecialConditions"},
                    {"KART ID", "CardId"},
                    {"KART NO", "CardNo"},
                    {"İL", "ProvinceId"},
                    {"İLÇE", "DistrictId"},
                    {"ALT YÜKLENİCİLER", "Subcontractors"},
                    {"ALT YÜKLENİCİ", "Subcontractors"},
                    {"İŞKUR KAYDI", "HasIskurRecord"},
                    {"BANKA", "BANKA"},
                    {"HESAP ADI", "HESAP ADI"},
                    {"İBAN", "İBAN"}
                };

                // Excel dosyasını oku
                ExcelPackage.License.SetNonCommercialPersonal("WebScraper");

                // Headers değişkenini geniş scope'ta tanımla
                var headers = new List<string>();

                using (var package = new ExcelPackage(new System.IO.FileInfo(_excelFilePath)))
                {
                    var worksheet = package.Workbook.Worksheets[0]; // İlk worksheet'i al
                    if (worksheet == null)
                    {
                        MessageBox.Show("Excel dosyasında geçerli bir çalışma sayfası bulunamadı!", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // Başlık satırını oku (1. satır)
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
                            var excelColumnName = headers[col - 1];
                            var value = worksheet.Cells[row, col].Value?.ToString()?.Trim() ?? "";

                            // Excel sütun adını FieldName'e çevir
                            if (columnMapping.ContainsKey(excelColumnName))
                            {
                                var fieldName = columnMapping[excelColumnName];
                                rowData[fieldName] = value;
                                Log($"Excel sütunu '{excelColumnName}' -> FieldName '{fieldName}': '{value}'");
                            }
                            else
                            {
                                // Map edilemeyen sütun için de ekle (esnek olsun)
                                rowData[excelColumnName] = value;
                                Log($"Map edilemeyen Excel sütunu: '{excelColumnName}' = '{value}'");
                            }
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
                    // Veri yoksa sadece uyarı göster
                    MessageBox.Show("Excel dosyasında personel verisi bulunamadı!\n\nLütfen personel bilgilerini manuel olarak ekleyin.", "Veri Bulunamadı", MessageBoxButton.OK, MessageBoxImage.Warning);

                    Log("⚠️ Excel dosyasında veri bulunamadı");
                    UpdateStatus("⚠️", "Veri Yok", "Excel dosyasında veri bulunamadı", "#FF9800", "#FFF3E0");
                    return;
                }

                // Dosya bilgisini göster
                txtSelectedFile.Text = System.IO.Path.GetFileName(_excelFilePath);

                // İşçi modu kontrolü - Excel'deki başlıklara göre mod belirle
                bool isWorkerMode = headers.Any(h => h.Contains("Firma") || h.Contains("Lokasyon") || h.Contains("Ekip Lideri"));
                _isSözleşmeliPersonelMode = !isWorkerMode;
                
                Log($"📋 Excel analizi: {(isWorkerMode ? "İşçi" : "Sözleşmeli Personel")} modu tespit edildi");
                Log($"🔧 _isSözleşmeliPersonelMode = {_isSözleşmeliPersonelMode}");
                
                // Tab başlığını güncelle
                UpdatePersonnelAddTabHeader();

                Log($"{_excelData.Count} personel verisi yüklendi. İşleme başlanabilir.");
                UpdateStatus("✅", "Hazır", $"{_excelData.Count} personel verisi yüklendi. İşleme başlanabilir.", "#4CAF50", "#E8F5E8");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Excel dosyası okunurken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                _useExcelData = false;
                btnFooterStart.IsEnabled = false;
            }
        }

        private void DownloadFixedTemplate()
        {
            try
            {
                // Kullanıcıya dosya kaydetme yeri sor
                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "İşçi Şablonunu Kaydet",
                    Filter = "Excel Files (*.xlsx)|*.xlsx",
                    FileName = "Isci_Personel_Sablonu.xlsx",
                    DefaultExt = ".xlsx",
                    AddExtension = true
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    string excelPath = saveFileDialog.FileName;

                    // EPPlus lisans ayarı
                    ExcelPackage.License.SetNonCommercialPersonal("WebScraper");

                    using (var package = new ExcelPackage())
                    {
                        var worksheet = package.Workbook.Worksheets.Add("İşçi Personeli");

                        // Hoş başlık tasarımı
                        var titleCell = worksheet.Cells[1, 1];
                        titleCell.Value = "İŞÇİ PERSONEL KAYIT ŞABLONU";
                        titleCell.Style.Font.Bold = true;
                        titleCell.Style.Font.Size = 16;
                        titleCell.Style.Font.Color.SetColor(System.Drawing.Color.White);
                        titleCell.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                        titleCell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(255, 152, 0));
                        titleCell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
                        worksheet.Cells[1, 1, 1, 14].Merge = true;

                        // Alt başlık
                        var subtitleCell = worksheet.Cells[2, 1];
                        subtitleCell.Value = "Güleryüz Group - Personel Bilgi Sistemi";
                        subtitleCell.Style.Font.Size = 10;
                        subtitleCell.Style.Font.Color.SetColor(System.Drawing.Color.Gray);
                        subtitleCell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
                        worksheet.Cells[2, 1, 2, 14].Merge = true;

                        // Başlıkları ekle (Excel sütunlarına göre) - 4. satırdan başla
                        var headers = new[] {
                            "TCKN",
                            "AD",
                            "SOYAD",
                            "CİNSİYET",
                            "MEDENİ DURUMU",
                            "ÇOCUK SAYISI",
                            "BABA ADI",
                            "ANA ADI",
                            "MESLEK",
                            "EMEKLİ Mİ?",
                            "İŞKUR KAYDI",
                            "BANKA",
                            "HESAP ADI",
                            "İBAN"
                        };

                        for (int i = 0; i < headers.Length; i++)
                        {
                            worksheet.Cells[4, i + 1].Value = headers[i];
                            worksheet.Cells[4, i + 1].Style.Font.Bold = true;
                            worksheet.Cells[4, i + 1].Style.Font.Color.SetColor(System.Drawing.Color.White);
                            worksheet.Cells[4, i + 1].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                            worksheet.Cells[4, i + 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(76, 175, 80));
                            worksheet.Cells[4, i + 1].Style.Border.BorderAround(OfficeOpenXml.Style.ExcelBorderStyle.Thin, System.Drawing.Color.LightGray);
                            worksheet.Column(i + 1).Width = 18;
                        }

                        // Örnek veri satırı ekle
                        var exampleData = new[] {
                            "12345678901",         // TCKN
                            "Ahmet",               // AD
                            "Yılmaz",              // SOYAD
                            "Erkek",               // CİNSİYET
                            "Evli",                // MEDENİ DURUMU
                            "2",                   // ÇOCUK SAYISI
                            "Mehmet",              // BABA ADI
                            "Fatma",               // ANA ADI
                            "Elektrikçi",          // MESLEK
                            "Hayır",               // EMEKLİ Mİ?
                            "Evet",                // İŞKUR KAYDI
                            "Ziraat Bankası",      // BANKA
                            "Ahmet Yılmaz",        // HESAP ADI
                            "TR123456789012345678901234" // İBAN
                        };

                        for (int i = 0; i < exampleData.Length; i++)
                        {
                            worksheet.Cells[2, i + 1].Value = exampleData[i];
                        }

                        package.SaveAs(new FileInfo(excelPath));
                    }

                    MessageBox.Show($"Personel şablonu başarıyla kaydedildi:\n{excelPath}",
                        "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);

                    // Dosyayı otomatik aç
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = excelPath,
                            UseShellExecute = true
                        });
                    }
                    catch
                    {
                        // Dosya açma başarısız olursa sessizce geç
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Excel şablonu oluşturulurken hata: {ex.Message}");
            }
        }



        // Geçmiş verilerini yükle
        private void LoadPersonnelHistory()
        {
            try
            {
                _personnelHistory = ConfigService.LoadConfig<PersonnelHistory>("personnel_history.json");
                if (_personnelHistory == null)
                {
                    _personnelHistory = new PersonnelHistory();
                }

                // DataGrid'i güncelle
                HistoryDataGrid.ItemsSource = _personnelHistory.Records.OrderByDescending(r => r.CreatedAt);

                // Filter'ları uygula
                ApplyHistoryFilters();

                Log($"📋 Personel geçmişi yüklendi: {_personnelHistory.Records.Count} kayıt");
            }
            catch (Exception ex)
            {
                Log($"❌ Geçmiş verileri yüklenirken hata: {ex.Message}");
                _personnelHistory = new PersonnelHistory();
            }
        }

        // Geçmiş verilerini kaydet
        private void SavePersonnelHistory()
        {
            try
            {
                ConfigService.SaveConfig("personnel_history.json", _personnelHistory);
            }
            catch (Exception ex)
            {
                Log($"❌ Geçmiş verileri kaydedilirken hata: {ex.Message}");
            }
        }

        // Geçmişe yeni kayıt ekle
        private void AddToHistory(Dictionary<string, string> data, string status, string notes = "")
        {
            try
            {
                var record = new PersonnelHistoryRecord
                {
                    TCKN = data.GetValueOrDefault("TCKN", ""),
                    FirstName = data.GetValueOrDefault("FirstName", ""),
                    LastName = data.GetValueOrDefault("LastName", ""),
                    Gender = data.GetValueOrDefault("Gender", ""),
                    Phone = data.GetValueOrDefault("Phone", ""),
                    Email = data.GetValueOrDefault("Email", ""),
                    BankName = data.GetValueOrDefault("BANKA", ""),
                    AccountName = data.GetValueOrDefault("HESAP ADI", ""),
                    IBAN = data.GetValueOrDefault("İBAN", ""),
                    PersonelTipi = _isSözleşmeliPersonelMode ? "Sözleşmeli Pers." : "İşçi",
                    Status = status,
                    Notes = notes,
                    CreatedAt = DateTime.Now
                };

                _personnelHistory.Records.Add(record);
                SavePersonnelHistory();

                // DataGrid'i güncelle
                HistoryDataGrid.ItemsSource = _personnelHistory.Records.OrderByDescending(r => r.CreatedAt);
            }
            catch (Exception ex)
            {
                Log($"❌ Geçmişe kayıt eklenirken hata: {ex.Message}");
            }
        }

        // Yenile butonu click handler
        private void btnRefreshHistory_Click(object sender, RoutedEventArgs e)
        {
            LoadPersonnelHistory();
            Log("🔄 Personel geçmişi yenilendi");
        }

        // Temizle butonu click handler


        // Geçmiş tab'ındaki ayarlar butonu
        private void btnHistorySettings_Click(object sender, RoutedEventArgs e)
        {
            MainTab.SelectedIndex = 2; // Ayarlar tab'ı
            Log("⚙️ Geçmiş tab'ından ayarlar tab'ına geçildi");
        }

        private void StartClockTimer()
        {
            // Saat gösterisi kaldırıldığı için timer'ı başlatmıyoruz
        }

        private void ClockTimer_Tick(object sender, EventArgs e)
        {
            // Saat gösterisi kaldırıldığı için boş metod
        }



        // Footer butonları - mevcut butonları çağırır
        private void btnFooterDownloadTemplateModal_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Modal göster
                var modal = new TemplateSelectionModal();
                modal.Owner = this;
                modal.WindowStartupLocation = WindowStartupLocation.CenterOwner;

                if (modal.ShowDialog() == true)
                {
                    // Modal'dan seçim yapıldı, şablonu indir
                    if (modal.SelectedTemplateType == TemplateSelectionModal.TemplateType.Worker)
                    {
                        DownloadFixedTemplate();
                    }
                    else if (modal.SelectedTemplateType == TemplateSelectionModal.TemplateType.Contract)
                    {
                        DownloadSozPersonelTemplate();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Şablon seçimi sırasında hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Cleanup - modal kapandı
            }
        }

        private async void btnConvertFromSystemTemplate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var convertModal = new SystemTemplateConverterModal();
                convertModal.Owner = this;
                if (convertModal.ShowDialog() == true)
                {
                    // Modal'dan seçilen kaynak dosya yolu ve tespit edilen tip
                    string sourceFilePath = convertModal.SourceFilePath;
                    var templateType = convertModal.DetectedTemplateType;
                    
                    if (!string.IsNullOrEmpty(sourceFilePath))
                    {
                        // Tespit edilen tipe göre uygun dönüştürmeyi yap
                        if (templateType == TemplateFileType.Worker)
                        {
                            await ConvertSystemTemplateToWorkerTemplate(sourceFilePath);
                        }
                        else if (templateType == TemplateFileType.Contract)
                        {
                            await ConvertSystemTemplateToSozPersonelTemplate(sourceFilePath);
                        }
                        else
                        {
                            // Bilinmeyen tip - varsayılan olarak sözleşmeli personel olarak dönüştür
                            await ConvertSystemTemplateToSozPersonelTemplate(sourceFilePath);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Sistem şablonu dönüştürme hatası: {ex.Message}");
                MessageBox.Show($"Şablon dönüştürme hatası: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ConvertSystemTemplateToSozPersonelTemplate(string sourceFilePath)
        {
            try
            {
                // Sistem şablonunu oku (seçilen dosyadan)
                var systemData = await ReadSystemTemplateData(sourceFilePath);

                if (systemData.Count == 0)
                {
                    MessageBox.Show("Sistem şablonunda veri bulunamadı!", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Sözleşmeli personel şablonuna dönüştür ve convert klasörüne kaydet
                string convertFilePath = await CreateSozPersonelTemplateFromSystemData(systemData);

                UpdateStatus("✅", "Dönüştürüldü", "Sistem şablonu başarıyla sözleşmeli personel şablonuna dönüştürüldü", "#4CAF50", "#E8F5E8");
                Log($"✅ Sistem şablonu dönüştürüldü");
                Log($"   Kaynak: {sourceFilePath}");
                Log($"   Convert klasörü: {convertFilePath}");

                MessageBox.Show($"Şablon başarıyla dönüştürüldü!\n\nKaynak Dosya: {System.IO.Path.GetFileName(sourceFilePath)}\nDönüştürülen: {convertFilePath}", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log($"❌ Şablon dönüştürme hatası: {ex.Message}");
                MessageBox.Show($"Şablon dönüştürme hatası: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task<List<Dictionary<string, string>>> ReadSystemTemplateData(string systemTemplatePath)
        {
            var data = new List<Dictionary<string, string>>();

            try
            {
                if (!System.IO.File.Exists(systemTemplatePath))
                {
                    throw new FileNotFoundException($"Sistem şablonu dosyası bulunamadı: {systemTemplatePath}");
                }

                using (var package = new ExcelPackage(new FileInfo(systemTemplatePath)))
                {
                    var worksheet = package.Workbook.Worksheets[0]; // İlk sayfa
                    var rowCount = worksheet.Dimension?.Rows ?? 0;
                    var colCount = worksheet.Dimension?.Columns ?? 0;

                    if (rowCount < 2) return data; // Başlık + en az 1 veri satırı olmalı

                    // Başlıkları oku (1. satır)
                    var headers = new List<string>();
                    for (int col = 1; col <= colCount; col++)
                    {
                        var headerValue = worksheet.Cells[1, col].Value?.ToString()?.Trim();
                        if (!string.IsNullOrEmpty(headerValue))
                        {
                            headers.Add(headerValue);
                            Log($"DEBUG: Sistem şablonu başlığı bulundu: '{headerValue}' (Sütun {col})");
                        }
                    }

                    // Veri satırlarını oku (2. satırdan itibaren)
                    for (int row = 2; row <= rowCount; row++)
                    {
                        var rowData = new Dictionary<string, string>();
                        bool hasData = false;

                        for (int col = 0; col < headers.Count && col < colCount; col++)
                        {
                            var cellValue = worksheet.Cells[row, col + 1].Value?.ToString()?.Trim();
                            if (!string.IsNullOrEmpty(cellValue))
                            {
                                rowData[headers[col]] = cellValue;
                                hasData = true;
                            }
                        }

                        if (hasData)
                        {
                            data.Add(rowData);
                        }
                    }
                }

                Log($"✅ Sistem şablonundan {data.Count} kayıt okundu");
            }
            catch (Exception ex)
            {
                Log($"❌ Sistem şablonu okuma hatası: {ex.Message}");
                throw;
            }

            return data;
        }

        private async Task<string> CreateSozPersonelTemplateFromSystemData(List<Dictionary<string, string>> systemData)
        {
            try
            {
                // Kök dizinde convert klasörü oluştur
                string rootDirectory = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                string convertDirectory = System.IO.Path.Combine(rootDirectory, "convert");
                
                Log($"DEBUG: Kök dizin: {rootDirectory}");
                Log($"DEBUG: Convert klasörü: {convertDirectory}");
                
                if (!System.IO.Directory.Exists(convertDirectory))
                {
                    System.IO.Directory.CreateDirectory(convertDirectory);
                    Log($"✅ Convert klasörü oluşturuldu: {convertDirectory}");
                }

                // Zaman damgalı dosya adı oluştur
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string convertFilePath = System.IO.Path.Combine(convertDirectory, $"Sozlesmeli_Personel_Sablonu_{timestamp}.xlsx");

                // Sözleşmeli personel şablonu alanları
                var sozPersonelFields = new[] {
                    "TCKN", "AD", "SOYAD", "CİNSİYET", "MEDENİ DURUMU", "ÇOCUK SAYISI",
                    "BABA ADI", "ANA ADI", "MESLEK", "EMEKLİ Mİ?", "DOĞUM TARİHİ",
                    "TEL NO", "E-POSTA", "ÖĞRENİM DURUMU", "ENGELLİ", "İŞKUR KAYDI",
                    "İL", "İLÇE", "AÇIK ADRES", "ALT YÜKLENİCİ", "BANKA", "HESAP ADI", "İBAN", "GEÇERLİ Mİ?"
                };

                using (var package = new ExcelPackage())
                {
                    var worksheet = package.Workbook.Worksheets.Add("Sözleşmeli Personel");

                    // Başlıkları yaz
                    for (int i = 0; i < sozPersonelFields.Length; i++)
                    {
                        worksheet.Cells[1, i + 1].Value = sozPersonelFields[i];
                            worksheet.Cells[1, i + 1].Style.Font.Bold = true;
                            worksheet.Cells[1, i + 1].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                            worksheet.Cells[1, i + 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGreen);
                            worksheet.Column(i + 1).Width = 20;
                        }

                    // Sistem verilerini dönüştür ve yaz
                    for (int row = 0; row < systemData.Count; row++)
                    {
                        var systemRow = systemData[row];
                        var mappedData = MapSystemDataToSozPersonel(systemRow);

                        for (int col = 0; col < sozPersonelFields.Length; col++)
                        {
                            var fieldName = sozPersonelFields[col];
                            if (mappedData.ContainsKey(fieldName))
                            {
                                worksheet.Cells[row + 2, col + 1].Value = mappedData[fieldName];
                            }
                        }
                    }

                    // Sadece convert klasörüne kaydet
                            package.SaveAs(new FileInfo(convertFilePath));
                            System.IO.File.SetLastWriteTime(convertFilePath, DateTime.Now);
                            System.IO.File.SetCreationTime(convertFilePath, DateTime.Now);
                            
                    Log($"✅ Dosya convert klasörüne kaydedildi: {convertFilePath}");
                }
                
                return convertFilePath; // Convert dosya yolunu döndür
            }
            catch (Exception ex)
            {
                Log($"❌ Sözleşmeli personel şablonu oluşturma hatası: {ex.Message}");
                throw;
            }
        }

        private Dictionary<string, string> MapSystemDataToSozPersonel(Dictionary<string, string> systemData)
        {
            var mappedData = new Dictionary<string, string>();

            // Sistem şablonundaki verileri logla
            Log($"DEBUG: Sistem verisi mapping başladı. Toplam alan: {systemData.Count}");
            foreach (var kvp in systemData)
            {
                Log($"DEBUG: Sistem alanı: '{kvp.Key}' = '{kvp.Value}'");
            }

            // Sistem şablonundaki gerçek alan adlarına göre doğrudan eşleştirme
            foreach (var systemField in systemData.Keys)
            {
                var systemValue = systemData[systemField];

                // Doğrudan alan eşleştirmeleri - sistem şablonundaki gerçek alan adlarına göre
                if (systemField == "TCKN")
                {
                    mappedData["TCKN"] = systemValue;
                    Log($"DEBUG: TCKN eşleştirildi: '{systemField}' -> '{systemValue}'");
                }
                else if (systemField == "Sicil No")
                {
                    // Sicil No bizim şablonda yok, boş bırakıyoruz
                    Log($"DEBUG: Sicil No atlandı: '{systemField}' -> '{systemValue}' (MESLEK alanına eşleştirilmiyor)");
                }
                else if (systemField == "AD" || systemField == "ADI" || systemField == "Adı")
                {
                    mappedData["AD"] = systemValue;
                    Log($"DEBUG: AD eşleştirildi: '{systemField}' -> '{systemValue}'");
                }
                else if (systemField == "SOYAD" || systemField == "SOYADI" || systemField == "Soyadı")
                {
                    mappedData["SOYAD"] = systemValue;
                    Log($"DEBUG: SOYAD eşleştirildi: '{systemField}' -> '{systemValue}'");
                }
                else if (systemField == "AdıSoyadı" || systemField == "Adı Soyadı")
                {
                    // Birleşik isim soyisim - ayır (geriye uyumluluk için)
                    var parts = systemValue.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        mappedData["AD"] = string.Join(" ", parts.Take(parts.Length - 1));
                        mappedData["SOYAD"] = parts.Last();
                        Log($"DEBUG: Birleşik isim ayrıldı: AD='{mappedData["AD"]}', SOYAD='{mappedData["SOYAD"]}'");
                    }
                    else
                    {
                        // Tek kelime ise tamamını ada koy
                        mappedData["AD"] = systemValue;
                        Log($"DEBUG: Tek kelime isim: AD='{systemValue}'");
                    }
                }
                else if (systemField == "Doğum Tar." || systemField == "Doğum Tarihi")
                {
                    // Doğum tarihi formatını düzelt (saat kısmını kaldır ve gün/ay formatını düzelt)
                    string formattedDate = FormatBirthDate(systemValue);
                    mappedData["DOĞUM TARİHİ"] = formattedDate;
                    Log($"DEBUG: DOĞUM TARİHİ eşleştirildi: '{systemField}' -> '{systemValue}' -> '{formattedDate}'");
                }
                else if (systemField == "Cinsiyet")
                {
                    mappedData["CİNSİYET"] = systemValue;
                    Log($"DEBUG: CİNSİYET eşleştirildi: '{systemField}' -> '{systemValue}'");
                }
                else if (systemField == "Baba Adı")
                {
                    mappedData["BABA ADI"] = systemValue;
                    Log($"DEBUG: BABA ADI eşleştirildi: '{systemField}' -> '{systemValue}'");
                }
                else if (systemField == "Ana Adı")
                {
                    mappedData["ANA ADI"] = systemValue;
                    Log($"DEBUG: ANA ADI eşleştirildi: '{systemField}' -> '{systemValue}'");
                }
                else if (systemField == "Hesap Adı")
                {
                    mappedData["HESAP ADI"] = systemValue;
                    Log($"DEBUG: HESAP ADI eşleştirildi: '{systemField}' -> '{systemValue}'");
                }
                else if (systemField == "IBAN")
                {
                    // IBAN'daki boşlukları kaldır
                    string cleanIban = systemValue?.Replace(" ", "") ?? "";
                    mappedData["İBAN"] = cleanIban;
                    Log($"DEBUG: İBAN eşleştirildi: '{systemField}' -> '{systemValue}' -> '{cleanIban}' (boşluklar kaldırıldı)");
                }
                else if (systemField == "Tel. No." || systemField == "Telefon")
                {
                    mappedData["TEL NO"] = systemValue;
                    Log($"DEBUG: TEL NO eşleştirildi: '{systemField}' -> '{systemValue}'");
                }
                else if (systemField == "Eposta" || systemField == "E-posta")
                {
                    mappedData["E-POSTA"] = systemValue;
                    Log($"DEBUG: E-POSTA eşleştirildi: '{systemField}' -> '{systemValue}'");
                }
                else if (systemField == "Engelli mi?")
                {
                    mappedData["ENGELLİ"] = systemValue;
                    Log($"DEBUG: ENGELLİ eşleştirildi: '{systemField}' -> '{systemValue}'");
                }
                else if (systemField == "Engeli")
                {
                    // Engeli bizim şablonda ayrı alan yok, ama engelli bilgisine ekleyebiliriz
                    if (mappedData.ContainsKey("ENGELLİ") && !string.IsNullOrEmpty(mappedData["ENGELLİ"]))
                    {
                        mappedData["ENGELLİ"] += $" ({systemValue})";
                    }
                    else
                    {
                        mappedData["ENGELLİ"] = systemValue;
                    }
                    Log($"DEBUG: Engeli bilgisi eklendi: '{systemField}' -> '{systemValue}'");
                }
                else if (systemField == "Alt yüklenici")
                {
                    mappedData["ALT YÜKLENİCİ"] = systemValue;
                    Log($"DEBUG: ALT YÜKLENİCİ eşleştirildi: '{systemField}' -> '{systemValue}'");
                }
                else if (systemField == "Emekli mi?")
                {
                    mappedData["EMEKLİ Mİ?"] = systemValue;
                    Log($"DEBUG: EMEKLİ Mİ? eşleştirildi: '{systemField}' -> '{systemValue}'");
                }
                else if (systemField == "Eğitim" || systemField == "Öğrenim")
                {
                    mappedData["ÖĞRENİM DURUMU"] = systemValue;
                    Log($"DEBUG: ÖĞRENİM DURUMU eşleştirildi: '{systemField}' -> '{systemValue}'");
                }
                else if (systemField == "Geçerli mi?")
                {
                    mappedData["GEÇERLİ Mİ?"] = systemValue;
                    Log($"DEBUG: GEÇERLİ Mİ? eşleştirildi: '{systemField}' -> '{systemValue}'");
                }
                else
                {
                    Log($"DEBUG: Eşleşme bulunamadı: '{systemField}' -> '{systemValue}'");
                }
            }

            // GEÇERLİ Mİ? alanını varsayılan olarak EVET yap
            mappedData["GEÇERLİ Mİ?"] = "EVET";
            
            // İl, İlçe ve Açık Adres için varsayılan değerler
            if (!mappedData.ContainsKey("İL") || string.IsNullOrWhiteSpace(mappedData["İL"]))
            {
                mappedData["İL"] = "İzmir";
                Log($"DEBUG: İL varsayılan değer atandı: İzmir");
            }
            
            if (!mappedData.ContainsKey("İLÇE") || string.IsNullOrWhiteSpace(mappedData["İLÇE"]))
            {
                mappedData["İLÇE"] = "Bornova";
                Log($"DEBUG: İLÇE varsayılan değer atandı: Bornova");
            }
            
            if (!mappedData.ContainsKey("AÇIK ADRES") || string.IsNullOrWhiteSpace(mappedData["AÇIK ADRES"]))
            {
                mappedData["AÇIK ADRES"] = "Bornova";
                Log($"DEBUG: AÇIK ADRES varsayılan değer atandı: Bornova");
            }
            
            if (!mappedData.ContainsKey("BANKA") || string.IsNullOrWhiteSpace(mappedData["BANKA"]))
            {
                mappedData["BANKA"] = "Ziraat Bankası";
                Log($"DEBUG: BANKA varsayılan değer atandı: Ziraat Bankası");
            }

            // ALT YÜKLENİCİ için varsayılan değer
            if (!mappedData.ContainsKey("ALT YÜKLENİCİ") || string.IsNullOrWhiteSpace(mappedData["ALT YÜKLENİCİ"]))
            {
                mappedData["ALT YÜKLENİCİ"] = "Varsayılan Alt Yüklenici";
                Log($"DEBUG: ALT YÜKLENİCİ varsayılan değer atandı: Varsayılan Alt Yüklenici");
            }

            // İŞKUR KAYDI her zaman EVET
            mappedData["İŞKUR KAYDI"] = "EVET";
            Log($"DEBUG: İŞKUR KAYDI varsayılan değer atandı: EVET");

            Log($"DEBUG: Mapping tamamlandı. Eşleştirilen alan sayısı: {mappedData.Count - 7}"); // -7 çünkü varsayılan değerler var
            return mappedData;
        }

        // İşçi şablonuna dönüştürme metotları
        private async Task ConvertSystemTemplateToWorkerTemplate(string sourceFilePath)
        {
            try
            {
                // Sistem şablonunu oku (seçilen dosyadan)
                var systemData = await ReadSystemTemplateData(sourceFilePath);

                if (systemData.Count == 0)
                {
                    MessageBox.Show("Sistem şablonunda veri bulunamadı!", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // İşçi şablonuna dönüştür ve convert klasörüne kaydet
                string convertFilePath = await CreateWorkerTemplateFromSystemData(systemData);

                UpdateStatus("✅", "Dönüştürüldü", "Sistem şablonu başarıyla işçi şablonuna dönüştürüldü", "#4CAF50", "#E8F5E8");
                Log($"✅ Sistem şablonu işçi formatına dönüştürüldü");
                Log($"   Kaynak: {sourceFilePath}");
                Log($"   Convert klasörü: {convertFilePath}");

                MessageBox.Show($"Şablon başarıyla işçi formatına dönüştürüldü!\n\nKaynak Dosya: {System.IO.Path.GetFileName(sourceFilePath)}\nDönüştürülen: {convertFilePath}", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log($"❌ İşçi şablonu dönüştürme hatası: {ex.Message}");
                MessageBox.Show($"İşçi şablonu dönüştürme hatası: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task<string> CreateWorkerTemplateFromSystemData(List<Dictionary<string, string>> systemData)
        {
            try
            {
                // Kök dizinde convert klasörü oluştur
                string rootDirectory = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                string convertDirectory = System.IO.Path.Combine(rootDirectory, "convert");
                
                Log($"DEBUG: Kök dizin: {rootDirectory}");
                Log($"DEBUG: Convert klasörü: {convertDirectory}");
                
                if (!System.IO.Directory.Exists(convertDirectory))
                {
                    System.IO.Directory.CreateDirectory(convertDirectory);
                    Log($"✅ Convert klasörü oluşturuldu: {convertDirectory}");
                }

                // Zaman damgalı dosya adı oluştur
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string convertFilePath = System.IO.Path.Combine(convertDirectory, $"Isci_Personel_Sablonu_{timestamp}.xlsx");

                // İşçi personel şablonu alanları
                var workerFields = new[] {
                    "TCKN", "AD", "SOYAD", "CİNSİYET", "MEDENİ DURUMU", "ÇOCUK SAYISI",
                    "BABA ADI", "ANA ADI", "DOĞUM TARİHİ", "TEL NO", "E-POSTA",
                    "ÖĞRENİM DURUMU", "ENGELLİ", "İŞKUR KAYDI", "EMEKLİ Mİ?",
                    "İL", "İLÇE", "AÇIK ADRES", "EKİP LİDERİ",
                    "BANKA", "HESAP ADI", "İBAN", "GEÇERLİ Mİ?"
                };

                using (var package = new ExcelPackage())
                {
                    var worksheet = package.Workbook.Worksheets.Add("İşçi Personeli");

                    // Başlıkları yaz
                    for (int i = 0; i < workerFields.Length; i++)
                    {
                        worksheet.Cells[1, i + 1].Value = workerFields[i];
                        worksheet.Cells[1, i + 1].Style.Font.Bold = true;
                        worksheet.Cells[1, i + 1].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                        worksheet.Cells[1, i + 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightBlue);
                        worksheet.Column(i + 1).Width = 20;
                    }

                    // Sistem verilerini dönüştür ve yaz
                    for (int row = 0; row < systemData.Count; row++)
                    {
                        var systemRow = systemData[row];
                        var mappedData = MapSystemDataToWorker(systemRow);

                        for (int col = 0; col < workerFields.Length; col++)
                        {
                            var fieldName = workerFields[col];
                            if (mappedData.ContainsKey(fieldName))
                            {
                                worksheet.Cells[row + 2, col + 1].Value = mappedData[fieldName];
                            }
                        }
                    }

                    // Sadece convert klasörüne kaydet
                    package.SaveAs(new FileInfo(convertFilePath));
                    System.IO.File.SetLastWriteTime(convertFilePath, DateTime.Now);
                    System.IO.File.SetCreationTime(convertFilePath, DateTime.Now);
                    
                    Log($"✅ Dosya convert klasörüne kaydedildi: {convertFilePath}");
                }
                
                return convertFilePath;
            }
            catch (Exception ex)
            {
                Log($"❌ İşçi personel şablonu oluşturma hatası: {ex.Message}");
                throw;
            }
        }

        private Dictionary<string, string> MapSystemDataToWorker(Dictionary<string, string> systemData)
        {
            var mappedData = new Dictionary<string, string>();

            Log($"DEBUG: İşçi mapping başladı. Toplam alan: {systemData.Count}");

            // Sistem şablonundaki gerçek alan adlarına göre doğrudan eşleştirme
            foreach (var systemField in systemData.Keys)
            {
                var systemValue = systemData[systemField];

                if (systemField == "TCKN")
                {
                    mappedData["TCKN"] = systemValue;
                }
                else if (systemField == "Adı" || systemField == "AD" || systemField == "ADI")
                {
                    mappedData["AD"] = systemValue;
                }
                else if (systemField == "Soyadı" || systemField == "SOYAD" || systemField == "SOYADI")
                {
                    mappedData["SOYAD"] = systemValue;
                }
                else if (systemField == "Cinsiyet" || systemField == "CİNSİYET")
                {
                    mappedData["CİNSİYET"] = systemValue;
                }
                else if (systemField == "Doğum Tar." || systemField == "DOĞUM TARİHİ" || systemField == "Doğum Tarihi")
                {
                    mappedData["DOĞUM TARİHİ"] = systemValue;
                }
                else if (systemField == "Baba Adı" || systemField == "BABA ADI")
                {
                    mappedData["BABA ADI"] = systemValue;
                }
                else if (systemField == "Ana Adı" || systemField == "ANA ADI")
                {
                    mappedData["ANA ADI"] = systemValue;
                }
                else if (systemField == "Tel. No." || systemField == "TEL NO" || systemField == "Telefon")
                {
                    mappedData["TEL NO"] = systemValue;
                }
                else if (systemField == "Eposta" || systemField == "E-POSTA" || systemField == "Email")
                {
                    mappedData["E-POSTA"] = systemValue;
                }
                else if (systemField == "Eğitim" || systemField == "ÖĞRENİM DURUMU" || systemField == "Öğrenim")
                {
                    mappedData["ÖĞRENİM DURUMU"] = systemValue;
                }
                else if (systemField == "Engelli mi?" || systemField == "ENGELLİ")
                {
                    mappedData["ENGELLİ"] = systemValue;
                }
                else if (systemField == "Engeli" && mappedData.ContainsKey("ENGELLİ"))
                {
                    mappedData["ENGELLİ"] += $" ({systemValue})";
                }
                else if (systemField == "Emekli mi?" || systemField == "EMEKLİ Mİ?")
                {
                    mappedData["EMEKLİ Mİ?"] = systemValue;
                }
                else if (systemField == "Firma" || systemField == "FİRMA")
                {
                    // FİRMA alanını görmezden gel
                    Log($"DEBUG: FİRMA alanı atlandı: '{systemField}' -> '{systemValue}'");
                }
                else if (systemField == "Lokasyon" || systemField == "LOKASYON")
                {
                    // LOKASYON alanını görmezden gel
                    Log($"DEBUG: LOKASYON alanı atlandı: '{systemField}' -> '{systemValue}'");
                }
                else if (systemField == "Ekip Lideri" || systemField == "EKİP LİDERİ")
                {
                    // Boş değilse ekle
                    if (!string.IsNullOrWhiteSpace(systemValue))
                    {
                        mappedData["EKİP LİDERİ"] = systemValue;
                        Log($"DEBUG: EKİP LİDERİ eşleştirildi: '{systemField}' -> '{systemValue}'");
                    }
                    else
                    {
                        Log($"DEBUG: EKİP LİDERİ boş geldi: '{systemField}'");
                    }
                }
                else if (systemField == "Hesap Adı" || systemField == "HESAP ADI")
                {
                    mappedData["HESAP ADI"] = systemValue;
                }
                else if (systemField == "IBAN" || systemField == "İBAN")
                {
                    mappedData["İBAN"] = systemValue;
                }
                else if (systemField == "HES")
                {
                    // HES kodunu şimdilik atla veya not olarak ekle
                    Log($"DEBUG: HES kodu bulundu: {systemValue}");
                }
                else if (systemField == "Geçerli mi?" || systemField == "GEÇERLİ Mİ?")
                {
                    mappedData["GEÇERLİ Mİ?"] = systemValue;
                }
            }

            // Varsayılan değerler
            mappedData["GEÇERLİ Mİ?"] = "EVET";
            
            // İŞKUR KAYDI her zaman EVET
            mappedData["İŞKUR KAYDI"] = "EVET";

            if (!mappedData.ContainsKey("İL") || string.IsNullOrWhiteSpace(mappedData["İL"]))
            {
                mappedData["İL"] = "İzmir";
            }

            if (!mappedData.ContainsKey("İLÇE") || string.IsNullOrWhiteSpace(mappedData["İLÇE"]))
            {
                mappedData["İLÇE"] = "Bornova";
            }
            
            if (!mappedData.ContainsKey("AÇIK ADRES") || string.IsNullOrWhiteSpace(mappedData["AÇIK ADRES"]))
            {
                mappedData["AÇIK ADRES"] = "Bornova";
            }
            
            if (!mappedData.ContainsKey("BANKA") || string.IsNullOrWhiteSpace(mappedData["BANKA"]))
            {
                mappedData["BANKA"] = "Ziraat Bankası";
            }

            // EKİP LİDERİ için varsayılan değer
            if (!mappedData.ContainsKey("EKİP LİDERİ") || string.IsNullOrWhiteSpace(mappedData["EKİP LİDERİ"]))
            {
                mappedData["EKİP LİDERİ"] = "Varsayılan Alt Yüklenici";
                Log($"DEBUG: EKİP LİDERİ varsayılan değer atandı: Varsayılan Alt Yüklenici");
            }

            Log($"DEBUG: İşçi mapping tamamlandı. Eşleştirilen alan sayısı: {mappedData.Count}");
            return mappedData;
        }

        private void DownloadSozPersonelTemplate()
        {
            try
            {
                // Debug: Çalışma dizinini logla
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                Log($"DEBUG: BaseDirectory = {baseDir}");

                // Proje dizinindeki hazır şablonu kullan (bin dizininin üst dizininden)
                string sourceTemplatePath = null;

                // Önce executable'ın bulunduğu dizinde ara
                string exeDirPath = System.IO.Path.Combine(baseDir, "Personel_Sablonu - Sozlesmeli.xlsx");
                Log($"DEBUG: exeDirPath = {exeDirPath}, Exists = {System.IO.File.Exists(exeDirPath)}");

                if (System.IO.File.Exists(exeDirPath))
                {
                    sourceTemplatePath = exeDirPath;
                    Log($"DEBUG: Şablon exe dizininde bulundu: {sourceTemplatePath}");
                }
                else
                {
                    // Üst dizinde ara (proje ana dizini)
                    string parentDir = System.IO.Directory.GetParent(baseDir)?.Parent?.FullName;
                    Log($"DEBUG: parentDir = {parentDir}");

                    if (parentDir != null)
                    {
                        string projectPath = System.IO.Path.Combine(parentDir, "Personel_Sablonu - Sozlesmeli.xlsx");
                        Log($"DEBUG: projectPath = {projectPath}, Exists = {System.IO.File.Exists(projectPath)}");

                        if (System.IO.File.Exists(projectPath))
                        {
                            sourceTemplatePath = projectPath;
                            Log($"DEBUG: Şablon proje dizininde bulundu: {sourceTemplatePath}");
                        }
                    }
                }

                if (sourceTemplatePath == null || !System.IO.File.Exists(sourceTemplatePath))
                {
                    string errorMsg = $"Şablon dosyası bulunamadı!\n\nAranan konumlar:\n1. {exeDirPath}\n2. {System.IO.Path.Combine(System.IO.Directory.GetParent(baseDir)?.Parent?.FullName ?? "", "Personel_Sablonu - Sozlesmeli.xlsx")}\n\nBaseDirectory: {baseDir}";
                    Log($"ERROR: {errorMsg}");
                    MessageBox.Show(errorMsg, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Kullanıcıya dosya kaydetme yeri sor
                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Sözleşmeli Personel Şablonunu Kaydet",
                    Filter = "Excel Files (*.xlsx)|*.xlsx",
                    FileName = "Personel_Sablonu - Sozlesmeli.xlsx",
                    DefaultExt = ".xlsx",
                    AddExtension = true
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    string excelPath = saveFileDialog.FileName;

                    try
                    {
                        // Hazır şablonu hedefe kopyala
                        System.IO.File.Copy(sourceTemplatePath, excelPath, true);

                        // Dosya tarihini güncelle (indirme tarihi olarak)
                        System.IO.File.SetLastWriteTime(excelPath, DateTime.Now);
                        System.IO.File.SetCreationTime(excelPath, DateTime.Now);

                        // Kopyalama başarılı mı kontrol et
                        if (System.IO.File.Exists(excelPath))
                        {
                            UpdateStatus("✅", "İndirildi", $"Sözleşmeli personel şablonu başarıyla indirildi\nKonum: {excelPath}", "#4CAF50", "#E8F5E8");
                        Log($"✅ Sözleşmeli personel şablonu kaydedildi: {excelPath}");

                            // Başarı mesajı göster
                            System.Windows.MessageBox.Show($"Şablon başarıyla kaydedildi!\nKonum: {excelPath}", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else
                        {
                            throw new Exception("Dosya kopyalandıktan sonra hedef konumda bulunamadı");
                        }
                    }
                    catch (Exception copyEx)
                    {
                        Log($"❌ Şablon kopyalama hatası: {copyEx.Message}");
                        MessageBox.Show($"Şablon kaydedilirken hata oluştu: {copyEx.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Sözleşmeli personel şablonu indirilirken hata: {ex.Message}");
                MessageBox.Show($"Şablon indirilirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnFooterDownloadSozPersonelTemplate_Click(object sender, RoutedEventArgs e)
        {
            DownloadSozPersonelTemplate();
            Log("📋 Sözleşmeli personel şablonu indirildi");
        }

        private void btnOpenConvertFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Convert klasörü yolunu oluştur
                string rootDirectory = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                string convertDirectory = System.IO.Path.Combine(rootDirectory, "convert");
                
                // Klasör yoksa oluştur
                if (!System.IO.Directory.Exists(convertDirectory))
                {
                    System.IO.Directory.CreateDirectory(convertDirectory);
                    Log($"✅ Convert klasörü oluşturuldu: {convertDirectory}");
                }
                
                // Windows Explorer'da klasörü aç
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
                {
                    FileName = convertDirectory,
                    UseShellExecute = true,
                    Verb = "open"
                });
                
                Log($"📁 Convert klasörü açıldı: {convertDirectory}");
            }
            catch (Exception ex)
            {
                Log($"❌ Convert klasörü açma hatası: {ex.Message}");
                MessageBox.Show($"Convert klasörü açılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnFooterCancel_Click(object sender, RoutedEventArgs e)
        {
            // MessageBox ile kapatma onayı al
            var result = MessageBox.Show(
                "Uygulamayı kapatmak istediğinizden emin misiniz?",
                "Kapatma Onayı",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No
            );

            if (result == MessageBoxResult.Yes)
            {
                CancelButton_Click(sender, e);
            }
        }

        private void btnFooterStart_Click(object sender, RoutedEventArgs e)
        {
            StartProcessButton_Click(sender, e);
        }

        // Log butonları
        private void btnClearLogs_Click(object sender, RoutedEventArgs e)
        {
            LogTextBox.Clear();
            Log("🗑️ Loglar temizlendi");
        }

        private void btnCopyLogs_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(LogTextBox.Text))
            {
                System.Windows.Clipboard.SetText(LogTextBox.Text);
                Log("📋 Loglar panoya kopyalandı");
                UpdateStatus("✅", "Kopyalandı", "Loglar panoya kopyalandı", "#4CAF50", "#E8F5E8");
            }
            else
            {
                Log("⚠️ Kopyalanacak log bulunamadı");
                UpdateStatus("⚠️", "Uyarı", "Kopyalanacak log bulunamadı", "#FF9800", "#FFF3E0");
            }
        }

        private void btnSaveLogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(LogTextBox.Text))
                {
                    Log("⚠️ Kaydedilecek log bulunamadı");
                    UpdateStatus("⚠️", "Uyarı", "Kaydedilecek log bulunamadı", "#FF9800", "#FFF3E0");
                    return;
                }

                // Save file dialog
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Log Dosyasını Kaydet",
                    Filter = "Text Files (*.txt)|*.txt|Log Files (*.log)|*.log|All Files (*.*)|*.*",
                    DefaultExt = ".txt",
                    FileName = $"personnel_logs_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    System.IO.File.WriteAllText(saveDialog.FileName, LogTextBox.Text);
                    Log($"💾 Loglar kaydedildi: {saveDialog.FileName}");
                    UpdateStatus("✅", "Kaydedildi", $"Loglar kaydedildi: {System.IO.Path.GetFileName(saveDialog.FileName)}", "#4CAF50", "#E8F5E8");
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Log kaydetme hatası: {ex.Message}");
                UpdateStatus("❌", "Hata", $"Log kaydetme hatası: {ex.Message}", "#F44336", "#FFEBEE");
            }
        }



        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // Diğer kaynakları temizle
            _clockTimer?.Stop();
            CleanupBrowserAsync().Wait();
        }

        // Geçmiş Filter Event Handlers
        private void txtHistoryTcknFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyHistoryFilters();
        }

        private void cmbHistoryPersonelTipiFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyHistoryFilters();
        }

        private void btnClearHistoryFilters_Click(object sender, RoutedEventArgs e)
        {
            txtHistoryTcknFilter.Text = "";
            cmbHistoryPersonelTipiFilter.SelectedIndex = 0; // Tümünü seç
            ApplyHistoryFilters();
        }

        private void ApplyHistoryFilters()
        {
            try
            {
                if (_personnelHistory?.Records == null)
                    return;

                var filteredRecords = _personnelHistory.Records.AsQueryable();

                // TCKN filter
                var tcknFilter = txtHistoryTcknFilter.Text?.Trim() ?? "";
                if (!string.IsNullOrEmpty(tcknFilter))
                {
                    filteredRecords = filteredRecords.Where(r => r.TCKN.Contains(tcknFilter, StringComparison.OrdinalIgnoreCase));
                }

                // Personel Tipi filter
                if (cmbHistoryPersonelTipiFilter.SelectedItem is ComboBoxItem selectedItem)
                {
                    var personelTipiFilter = selectedItem.Content.ToString();
                    if (personelTipiFilter != "Tümü")
                    {
                        filteredRecords = filteredRecords.Where(r => r.PersonelTipi == personelTipiFilter);
                    }
                }

                // DataGrid'i güncelle
                HistoryDataGrid.ItemsSource = filteredRecords.OrderByDescending(r => r.CreatedAt).ToList();
            }
            catch (Exception ex)
            {
                Log($"❌ Geçmiş filter uygulanırken hata: {ex.Message}");
            }
        }

        // TOTP Secret görünürlük toggle
        // TOTP değerini almak için property (basitleştirilmiş - sadece txtTotpSecret kullanıyor)
        public string TotpSecret
        {
            get
            {
                return txtTotpSecret?.Text ?? "";
            }
            set
            {
                if (txtTotpSecret != null)
                    txtTotpSecret.Text = value;
            }
        }



        private void btnFooterDownloadTemplate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Şablon türü seçimi modal'ı göster
                var modal = new TemplateSelectionModal();
                if (modal.ShowDialog() == true)
                {
                    var templateType = modal.SelectedTemplateType;
                    Log($"Şablon türü seçildi: {templateType}");
                    
                    if (templateType == TemplateSelectionModal.TemplateType.Worker)
                    {
                        DownloadFixedTemplate();
                    }
                    else if (templateType == TemplateSelectionModal.TemplateType.Contract)
                    {
                        DownloadSozPersonelTemplate();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Şablon indirme hatası: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                Log($"Şablon indirme hatası: {ex.Message}");
            }
        }

        private string FormatBirthDate(string dateValue)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dateValue))
                    return string.Empty;

                // DateTime olarak parse etmeye çalış
                if (DateTime.TryParse(dateValue, out DateTime date))
                {
                    // dd.MM.yyyy formatında döndür (saat kısmını kaldır)
                    return date.ToString("dd.MM.yyyy");
                }

                // Eğer parse edilemezse orijinal değeri döndür
                return dateValue;
            }
            catch
            {
                return dateValue;
            }
        }

    }
}
