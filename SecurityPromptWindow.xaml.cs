using System;
using System.Windows;
using System.Windows.Input;

namespace WebScraper
{
    public partial class SecurityPromptWindow : Window
    {
        private readonly SecurityProfileService _service;
        private readonly SecurityProfile _profile;

        public SecurityPromptWindow(SecurityProfileService service, SecurityProfile profile)
        {
            InitializeComponent();
            _service = service;
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));

            if (_profile.SecurityQuestions != null && _profile.SecurityQuestions.Count >= 2)
            {
                question1Text.Text = _profile.SecurityQuestions[0].Question;
                question2Text.Text = _profile.SecurityQuestions[1].Question;
            }

            Loaded += SecurityPromptWindow_Loaded;
        }

        private void NumericOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = e.Text.Length > 0 && !char.IsDigit(e.Text[0]);
        }

        private void SecurityPromptWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            pinEntry.Focus();
            pinEntry.SelectAll();
        }

        private void PinEntry_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ValidatePin_Click(sender, new RoutedEventArgs());
            }
        }

        private void BackupCodeEntry_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ValidateBackupCode_Click(sender, new RoutedEventArgs());
            }
        }

        private void SecurityAnswers_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ValidateQuestions_Click(sender, new RoutedEventArgs());
            }
        }

        private void ValidatePin_Click(object sender, RoutedEventArgs e)
        {
            var pin = pinEntry.Password?.Trim();
            if (string.IsNullOrWhiteSpace(pin))
            {
                MessageBox.Show("PIN giriniz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_service.VerifyPin(_profile, pin))
            {
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("PIN hatalı.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ValidateBackupCode_Click(object sender, RoutedEventArgs e)
        {
            var code = backupCodeEntry.Password?.Trim();
            if (string.IsNullOrWhiteSpace(code))
            {
                MessageBox.Show("Yedek kod giriniz.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var (success, _) = _service.TryConsumeBackupCode(_profile, code);
            if (success)
            {
                MessageBox.Show("Yedek kod kullanıldı. Lütfen yenilerini oluşturmayı unutmayın.", "Bilgi",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Yedek kod geçersiz veya kullanılmış.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ValidateQuestions_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(answer1Entry.Text) || string.IsNullOrWhiteSpace(answer2Entry.Text))
            {
                MessageBox.Show("Her iki soruyu da cevaplayın.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_service.VerifySecurityQuestions(_profile, answer1Entry.Text, answer2Entry.Text))
            {
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Yanlış cevap.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

