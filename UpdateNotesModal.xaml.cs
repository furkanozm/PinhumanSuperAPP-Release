using System;
using System.Windows;

namespace WebScraper
{
    public partial class UpdateNotesModal : Window
    {
        public Action? OnDownloadRequested { get; set; }

        public UpdateNotesModal()
        {
            InitializeComponent();
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void btnDownload_Click(object sender, RoutedEventArgs e)
        {
            OnDownloadRequested?.Invoke();
            this.Close();
        }
    }
}

