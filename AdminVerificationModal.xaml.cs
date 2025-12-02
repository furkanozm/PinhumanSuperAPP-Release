using System;
using System.Windows;
using System.Windows.Input;

namespace WebScraper
{
    public partial class AdminVerificationModal : Window
    {
        private readonly SecurityProfileService securityService;
        private SecurityProfile? securityProfile;

        public bool IsVerified { get; private set; } = false;

        public AdminVerificationModal()
        {
            InitializeComponent();
            securityService = new SecurityProfileService();
            securityProfile = securityService.LoadProfile();
            
            // PIN profili yoksa hata göster
            if (securityProfile == null || string.IsNullOrWhiteSpace(securityProfile.PinHash))
            {
                ShowError("PIN kodu ayarlanmamış. Lütfen önce güvenlik ayarlarından PIN kodu oluşturun.");
                btnVerify.IsEnabled = false;
                return;
            }
            
            // Window yüklendikten sonra PIN alanına odaklan
            this.Loaded += (s, e) =>
            {
                txtPinCode?.Focus();
            };
        }

        private void btnVerify_Click(object sender, RoutedEventArgs e)
        {
            VerifyPin();
        }

        private void txtPinCode_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                VerifyPin();
            }
        }

        private void VerifyPin()
        {
            bool shouldReEnableButton = true;
            
            try
            {
                if (txtErrorMessage != null)
                {
                    txtErrorMessage.Visibility = Visibility.Collapsed;
                }
                if (btnVerify != null)
                {
                    btnVerify.IsEnabled = false;
                }

                if (securityProfile == null)
                {
                    ShowError("PIN kodu profili bulunamadı.");
                    return;
                }

                string pin = txtPinCode?.Password ?? string.Empty;

                if (string.IsNullOrWhiteSpace(pin))
                {
                    ShowError("Lütfen PIN kodunu girin.");
                    if (btnVerify != null)
                    {
                        btnVerify.IsEnabled = true;
                    }
                    return;
                }

                // PIN doğrulama
                bool success = securityService.VerifyPin(securityProfile, pin);

                if (success)
                {
                    IsVerified = true;
                    DialogResult = true;
                    shouldReEnableButton = false;
                    this.Close();
                    return;
                }
                else
                {
                    ShowError("PIN kodu hatalı. Lütfen tekrar deneyin.");
                    txtPinCode?.Clear();
                    txtPinCode?.Focus();
                }
            }
            catch (Exception ex)
            {
                ShowError($"Doğrulama sırasında hata oluştu: {ex.Message}");
            }
            finally
            {
                if (shouldReEnableButton && btnVerify != null)
                {
                    btnVerify.IsEnabled = true;
                }
            }
        }

        private void ShowError(string message)
        {
            if (txtErrorMessage != null)
            {
                txtErrorMessage.Text = message;
                txtErrorMessage.Visibility = Visibility.Visible;
            }
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

