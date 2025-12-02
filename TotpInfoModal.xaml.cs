using System.Windows;

namespace WebScraper
{
    public partial class TotpInfoModal : Window
    {
        public TotpInfoModal()
        {
            InitializeComponent();
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
} 