using System;
using System.Windows;

namespace WebScraper
{
    public partial class JobEntryModal : Window
    {
        public JobEntryModal()
        {
            InitializeComponent();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            // İşe giriş işlemi burada yapılacak
            // Şimdilik sadece modal'ı kapat
            DialogResult = true;
            Close();
        }
    }
}
