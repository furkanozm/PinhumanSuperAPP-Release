using System.Diagnostics;
using System.Windows;
using System.IO;

namespace WebScraper
{
    public partial class UserRequestModal : Window
    {
        
        public UserRequestModal()
        {
            InitializeComponent();
        }
        
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            this.DragMove();
        }

        private void SubmitRequest_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Form validasyonu
                if (string.IsNullOrWhiteSpace(txtEmail.Text))
                {
                    System.Windows.MessageBox.Show("Email adresi gereklidir.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtEmail.Focus();
                    return;
                }
            
                if (string.IsNullOrWhiteSpace(txtFullName.Text))
                {
                    System.Windows.MessageBox.Show("Ad Soyad gereklidir.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtFullName.Focus();
                    return;
                }
                
                if (string.IsNullOrWhiteSpace(txtPhone.Text))
                {
                    System.Windows.MessageBox.Show("GSM numarası gereklidir.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtPhone.Focus();
                    return;
                }
                
                // Email format kontrolü
                try
                {
                    var addr = new System.Net.Mail.MailAddress(txtEmail.Text);
                    if (addr.Address != txtEmail.Text)
                    {
                        System.Windows.MessageBox.Show("Geçerli bir email adresi giriniz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                        txtEmail.Focus();
                        return;
                    }
                }
                catch
                {
                    System.Windows.MessageBox.Show("Geçerli bir email adresi giriniz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtEmail.Focus();
                    return;
                }
                
                // Kullanıcı talebini oluştur
                var userRequest = CreateUserRequestReport();
                
                // Outlook Classic ile mail gönder
                SendUserRequestViaOutlook(userRequest);
                
                System.Windows.MessageBox.Show("Kullanıcı talebiniz Outlook Classic ile açıldı. Maili gönderdikten sonra pencereyi kapatabilirsiniz.", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                this.Close();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Talep gönderilirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string CreateUserRequestReport()
        {
            var report = new System.Text.StringBuilder();
            report.AppendLine("=== KULLANICI TALEBİ ===");
            report.AppendLine($"Tarih/Saat: {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
            report.AppendLine($"Bilgisayar: {Environment.MachineName}");
            report.AppendLine($"İşletim Sistemi: {Environment.OSVersion}");
            report.AppendLine($"Uygulama: Ödeme Emri Oluşturucu v1.0");
            report.AppendLine();
            report.AppendLine("=== TALEP EDEN KULLANICI BİLGİLERİ ===");
            report.AppendLine($"Email: {txtEmail.Text.Trim()}");
            report.AppendLine($"Ad Soyad: {txtFullName.Text.Trim()}");
            report.AppendLine($"GSM: {txtPhone.Text.Trim()}");
            
            if (!string.IsNullOrWhiteSpace(txtDescription.Text))
            {
                report.AppendLine($"Açıklama: {txtDescription.Text.Trim()}");
            }
            
            report.AppendLine();
            report.AppendLine("=== SİSTEM BİLGİLERİ ===");
            report.AppendLine($"Çalışma Dizini: {Environment.CurrentDirectory}");
            report.AppendLine($"Bellek Kullanımı: {GC.GetTotalMemory(false) / 1024 / 1024} MB");
            
            return report.ToString();
        }

        private void SendUserRequestViaOutlook(string userRequest)
        {
            try
            {
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
                    var mailtoUrl = $"mailto:furkan.ozmen@guleryuzgroup.com;furkanozm@gmail.com?subject={Uri.EscapeDataString("Kullanıcı Talebi - Ödeme Emri Oluşturucu")}&body={Uri.EscapeDataString(userRequest)}";
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = foundOutlookPath,
                        Arguments = $"/c ipm.note /m \"{mailtoUrl}\"",
                        UseShellExecute = false
                    });
                    
                    // Otomatik mail gönderimi kaldırıldı - sadece manuel gönderim
                }
                else
                {
                    // Outlook bulunamazsa varsayılan mail uygulamasını kullan
                    var mailtoUrl = $"mailto:furkan.ozmen@guleryuzgroup.com;furkanozm@gmail.com?subject={Uri.EscapeDataString("Kullanıcı Talebi - Ödeme Emri Oluşturucu")}&body={Uri.EscapeDataString(userRequest)}";
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = mailtoUrl,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Outlook açılırken hata: {ex.Message}");
            }
        }
    }
}
