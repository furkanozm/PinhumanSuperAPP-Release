using System.Windows;
using System.Windows.Controls;
using OfficeOpenXml;
using System.IO;
using System.Linq;

namespace WebScraper
{
    public enum TemplateFileType
    {
        Worker,      // İşçi şablonu
        Contract,    // Sözleşmeli personel şablonu
        Unknown      // Bilinmeyen
    }

    public partial class SystemTemplateConverterModal : Window
    {
        public string SourceFilePath { get; private set; }
        public TemplateFileType DetectedTemplateType { get; private set; }

        public SystemTemplateConverterModal()
        {
            InitializeComponent();
            SourceFilePath = string.Empty;
            DetectedTemplateType = TemplateFileType.Unknown;
            txtOutputPath.Text = string.Empty;
            
            // EPPlus lisans ayarı
            ExcelPackage.License.SetNonCommercialPersonal("WebScraper");
        }

        private void btnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Dönüştürülecek Sistem Şablonunu Seçin",
                Filter = "Excel Files (*.xlsx;*.xls)|*.xlsx;*.xls|All Files (*.*)|*.*",
                DefaultExt = ".xlsx",
                Multiselect = false
            };

            if (openFileDialog.ShowDialog() == true)
            {
                txtOutputPath.Text = openFileDialog.FileName;
                
                // Dosya tipini tespit et
                DetectTemplateType(openFileDialog.FileName);
            }
        }

        private void DetectTemplateType(string filePath)
        {
            try
            {
                using (var package = new ExcelPackage(new FileInfo(filePath)))
                {
                    var worksheet = package.Workbook.Worksheets.FirstOrDefault();
                    if (worksheet == null || worksheet.Dimension == null)
                    {
                        DetectedTemplateType = TemplateFileType.Unknown;
                        txtStatus.Text = "⚠️ Excel dosyasında veri bulunamadı!";
                        return;
                    }

                    // İlk satırdaki başlıkları oku
                    var headers = new System.Collections.Generic.List<string>();
                    var colCount = worksheet.Dimension.Columns;
                    
                    for (int col = 1; col <= colCount; col++)
                    {
                        var headerValue = worksheet.Cells[1, col].Value?.ToString()?.Trim();
                        if (!string.IsNullOrEmpty(headerValue))
                        {
                            headers.Add(headerValue);
                        }
                    }

                    // Dosya tipini tespit et
                    // İşçi şablonu: Firma, Lokasyon, Ekip Lideri gibi alanlara sahip
                    bool hasWorkerFields = headers.Contains("Firma") || 
                                          headers.Contains("Lokasyon") || 
                                          headers.Contains("Ekip Lideri");

                    // Sözleşmeli şablonu: Alt yüklenici alanına sahip, Firma/Lokasyon/Ekip Lideri yok
                    bool hasContractFields = headers.Contains("Alt yüklenici") || 
                                            headers.Contains("Alt Yüklenici") ||
                                            headers.Contains("ALT YÜKLENİCİ");

                    if (hasWorkerFields)
                    {
                        DetectedTemplateType = TemplateFileType.Worker;
                        txtStatus.Text = "✅ Tespit Edilen Tip: İŞÇİ ŞABLONU";
                        txtStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Blue);
                    }
                    else if (hasContractFields || !hasWorkerFields)
                    {
                        DetectedTemplateType = TemplateFileType.Contract;
                        txtStatus.Text = "✅ Tespit Edilen Tip: SÖZLEŞMELİ PERSONEL ŞABLONU";
                        txtStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green);
                    }
                    else
                    {
                        DetectedTemplateType = TemplateFileType.Unknown;
                        txtStatus.Text = "⚠️ Şablon tipi tespit edilemedi!";
                        txtStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Orange);
                    }
                }
            }
            catch (System.Exception ex)
            {
                DetectedTemplateType = TemplateFileType.Unknown;
                txtStatus.Text = $"❌ Dosya okuma hatası: {ex.Message}";
                txtStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
            }
        }

        private void btnConvert_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtOutputPath.Text))
            {
                MessageBox.Show("Lütfen dönüştürülecek sistem şablonu dosyasını seçin!", "Uyarı",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!System.IO.File.Exists(txtOutputPath.Text))
            {
                MessageBox.Show("Seçilen dosya bulunamadı!", "Hata",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (DetectedTemplateType == TemplateFileType.Unknown)
            {
                var result = MessageBox.Show(
                    "Şablon tipi tespit edilemedi! Yine de dönüştürme işlemine devam etmek istiyor musunuz?",
                    "Bilinmeyen Şablon",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                
                if (result != MessageBoxResult.Yes)
                    return;
            }
            else
            {
                // Tespit edilen tipe göre onay mesajı
                string templateTypeName = DetectedTemplateType == TemplateFileType.Worker 
                    ? "İŞÇİ ŞABLONU" 
                    : "SÖZLEŞMELİ PERSONEL ŞABLONU";
                    
                var result = MessageBox.Show(
                    $"Tespit edilen şablon tipi: {templateTypeName}\n\nDönüştürme işlemine devam etmek istiyor musunuz?",
                    "Şablon Tespiti",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);
                
                if (result != MessageBoxResult.Yes)
                    return;
            }

            SourceFilePath = txtOutputPath.Text;
            DialogResult = true;
            Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        public void UpdateProgress(string message, bool showProgress = false)
        {
            Dispatcher.Invoke(() =>
            {
                txtStatus.Text = message;
                progressBar.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;
            });
        }
    }
}
