using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;

namespace WebScraper
{
    public partial class SecuritySetupWindow : Window
    {
        private readonly SecurityProfileService _service;
        private readonly SecurityProfile? _existingProfile;
        private List<string> _backupCodes = new List<string>();

        public SecurityProfile? ResultProfile { get; private set; }

        public SecuritySetupWindow(SecurityProfileService service, SecurityProfile? existingProfile = null)
        {
            InitializeComponent();
            _service = service;
            _existingProfile = existingProfile;
            GenerateBackupCodes();
            PopulateExistingData();
        }

        private void NumericOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = e.Text.Any(ch => !char.IsDigit(ch));
        }

        private void PopulateExistingData()
        {
            if (_existingProfile?.SecurityQuestions != null && _existingProfile.SecurityQuestions.Count >= 2)
            {
                txtQuestion1.Text = _existingProfile.SecurityQuestions[0].Question;
                txtQuestion2.Text = _existingProfile.SecurityQuestions[1].Question;
            }
        }

        private void GenerateBackupCodes_Click(object sender, RoutedEventArgs e)
        {
            GenerateBackupCodes();
        }

        private void GenerateBackupCodes()
        {
            _backupCodes = SecurityProfileService.GenerateBackupCodes();
            lstBackupCodes.ItemsSource = _backupCodes.ToList();
        }

        private bool TryExportBackupCodesToFile()
        {
            if (_backupCodes == null || _backupCodes.Count == 0)
            {
                MessageBox.Show("Önce yedek kodları oluşturun.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            var sfd = new SaveFileDialog
            {
                Title = "Yedek kodları kaydet",
                Filter = "Metin Dosyası (*.txt)|*.txt",
                FileName = $"yedek-kodlar-{DateTime.Now:yyyyMMddHHmm}.txt"
            };

            if (sfd.ShowDialog() != true)
            {
                return false;
            }

            var builder = new StringBuilder();
            builder.AppendLine("Güvenlik Yedek Kodlarınız");
            builder.AppendLine("--------------------------");
            foreach (var code in _backupCodes)
            {
                builder.AppendLine(code);
            }
            builder.AppendLine();
            builder.AppendLine("Bu kodları güvenli bir yerde saklayın.");

            System.IO.File.WriteAllText(sfd.FileName, builder.ToString());
            MessageBox.Show("Yedek kodlar TXT olarak indirildi.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var pin = txtPin.Password?.Trim();
            var pinConfirm = txtPinConfirm.Password?.Trim();

            if (string.IsNullOrWhiteSpace(pin) || pin.Length != 6)
            {
                MessageBox.Show("PIN 6 haneli olmalıdır.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!string.Equals(pin, pinConfirm, StringComparison.Ordinal))
            {
                MessageBox.Show("PIN doğrulaması eşleşmiyor.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(txtQuestion1.Text) || string.IsNullOrWhiteSpace(txtAnswer1.Text) ||
                string.IsNullOrWhiteSpace(txtQuestion2.Text) || string.IsNullOrWhiteSpace(txtAnswer2.Text))
            {
                MessageBox.Show("Lütfen iki güvenlik sorusunu da doldurun.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_backupCodes == null || _backupCodes.Count == 0)
            {
                MessageBox.Show("Yedek kodları oluşturmanız gerekir.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TryExportBackupCodesToFile())
            {
                MessageBox.Show("Yedek kodları TXT olarak indirmeden işlemi tamamlayamazsınız.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var profile = new SecurityProfile
            {
                PinHash = SecurityProfileService.Hash(pin),
                BackupCodes = _backupCodes.Select(code => new BackupCodeEntry
                {
                    CodeHash = SecurityProfileService.Hash(code),
                    IsUsed = false
                }).ToList(),
                SecurityQuestions = new List<SecurityQuestionEntry>
                {
                    new SecurityQuestionEntry
                    {
                        Question = txtQuestion1.Text.Trim(),
                        AnswerHash = SecurityProfileService.Hash(txtAnswer1.Text.Trim().ToLowerInvariant())
                    },
                    new SecurityQuestionEntry
                    {
                        Question = txtQuestion2.Text.Trim(),
                        AnswerHash = SecurityProfileService.Hash(txtAnswer2.Text.Trim().ToLowerInvariant())
                    }
                }
            };

            _service.SaveProfile(profile);
            ResultProfile = profile;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

