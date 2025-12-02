using System;
using System.Windows;
using System.Windows.Controls;

namespace WebScraper
{
    public partial class PageSizeSelectionModal : Window
    {
        public int SelectedPageSize { get; private set; }

        public PageSizeSelectionModal()
        {
            InitializeComponent();

            // Varsayılan olarak 5 öğeyi seç
            PageSizeComboBox.SelectedIndex = 0; // 5 öğe
        }

        private void PageSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PageSizeComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                var content = selectedItem.Content.ToString();

                // Seçilen değere göre page size'ı ayarla
                switch (content)
                {
                    case "5 öğe":
                        SelectedPageSize = 5;
                        UpdateWarningAndRecommendation(5);
                        break;
                    case "15 öğe":
                        SelectedPageSize = 15;
                        UpdateWarningAndRecommendation(15);
                        break;
                    case "60 öğe":
                        SelectedPageSize = 60;
                        UpdateWarningAndRecommendation(60);
                        break;
                    case "120 öğe":
                        SelectedPageSize = 120;
                        UpdateWarningAndRecommendation(120);
                        break;
                }

                // Başlat butonunu aktifleştir
                StartButton.IsEnabled = true;
            }
        }

        private void UpdateWarningAndRecommendation(int pageSize)
        {
            string warningText = "";

            switch (pageSize)
            {
                case 5:
                    warningText = "⚠️ Çok az öğe sayısı seçtiniz. Bu seçenek sadece çok az ödeme emriniz varsa uygundur.";
                    WarningBorder.Background = System.Windows.Media.Brushes.LightCoral;
                    WarningBorder.BorderBrush = System.Windows.Media.Brushes.Red;
                    break;

                case 15:
                    warningText = "⚠️ Orta düzey öğe sayısı. Dengeli bir seçim.";
                    WarningBorder.Background = System.Windows.Media.Brushes.LightYellow;
                    WarningBorder.BorderBrush = System.Windows.Media.Brushes.Orange;
                    break;

                case 60:
                    warningText = "✅ Optimum seçim! Hız ve kapsam dengesi mükemmel.";
                    WarningBorder.Background = System.Windows.Media.Brushes.LightGreen;
                    WarningBorder.BorderBrush = System.Windows.Media.Brushes.Green;
                    break;

                case 120:
                    warningText = "⚠️ Çok fazla öğe sayısı. İşlem uzun sürebilir.";
                    WarningBorder.Background = System.Windows.Media.Brushes.LightPink;
                    WarningBorder.BorderBrush = System.Windows.Media.Brushes.DarkRed;
                    break;
            }

            WarningTextBlock.Text = warningText;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedPageSize <= 0)
            {
                MessageBox.Show("Lütfen geçerli bir sayfa boyutu seçin!", "Uyarı",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            // Close(); // Modal kapanmayacak, sadece DialogResult true olacak
        }
    }
}
