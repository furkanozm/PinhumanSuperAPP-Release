using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace WebScraper
{
    public partial class TemplateUploadModal : Window
    {
        public string SelectedFilePath { get; private set; }

        public TemplateUploadModal(string initialPath = null)
        {
            InitializeComponent();

            if (!string.IsNullOrWhiteSpace(initialPath) && File.Exists(initialPath))
            {
                SelectedFilePath = initialPath;
                txtFilePath.Text = SelectedFilePath;
                btnConfirm.IsEnabled = true;
            }
        }

        private void btnBrowse_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new OpenFileDialog
                {
                    Title = "ERP Şablon Dosyasını Seçin",
                    Filter = "Excel Dosyaları (*.xlsx)|*.xlsx|Tüm Dosyalar (*.*)|*.*",
                    InitialDirectory = AppDomain.CurrentDomain.BaseDirectory
                };

                if (dialog.ShowDialog() == true)
                {
                    SelectedFilePath = dialog.FileName;
                    txtFilePath.Text = SelectedFilePath;
                    btnConfirm.IsEnabled = true;
                    HideValidation();
                }
            }
            catch (Exception ex)
            {
                ShowValidation($"Dosya seçilirken bir hata oluştu: {ex.Message}");
            }
        }

        private void btnConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SelectedFilePath))
            {
                ShowValidation("Lütfen bir dosya seçin.");
                return;
            }

            if (!File.Exists(SelectedFilePath))
            {
                ShowValidation("Seçilen dosya mevcut değil. Lütfen geçerli bir dosya seçin.");
                btnConfirm.IsEnabled = false;
                return;
            }

            DialogResult = true;
            Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ShowValidation(string message)
        {
            txtValidation.Text = message;
            txtValidation.Visibility = Visibility.Visible;
        }

        private void HideValidation()
        {
            txtValidation.Text = string.Empty;
            txtValidation.Visibility = Visibility.Collapsed;
        }
    }
}

