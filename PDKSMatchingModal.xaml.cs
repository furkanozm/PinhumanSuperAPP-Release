using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace WebScraper
{
    public partial class PDKSMatchingModal : Window
    {
        public List<PDKSMatchingRecord> PDKSMatchingRecords { get; private set; }

        public PDKSMatchingModal(List<PDKSMatchingRecord> pdksMatchingRecords)
        {
            InitializeComponent();
            PDKSMatchingRecords = pdksMatchingRecords ?? new List<PDKSMatchingRecord>();
            LoadPDKSMatchingData();
        }

        private void LoadPDKSMatchingData()
        {
            try
            {
                dgPDKSMatching.ItemsSource = PDKSMatchingRecords;

                // İstatistikleri hesapla
                int matchedCount = PDKSMatchingRecords.Count(r => r.IsMatched);
                int unmatchedCount = PDKSMatchingRecords.Count(r => !r.IsMatched);

                txtMatchedCount.Text = matchedCount.ToString();
                txtUnmatchedCount.Text = unmatchedCount.ToString();

                // Özet bilgi
                txtSummary.Text = $"Toplam {PDKSMatchingRecords.Count} personel bulundu";

                Console.WriteLine($"[PDKS Eşleştirme] {PDKSMatchingRecords.Count} personel gösteriliyor - Eşleşen: {matchedCount}, Eşleşmeyen: {unmatchedCount}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PDKS Eşleştirme] Veri yükleme hatası: {ex.Message}");
                MessageBox.Show($"PDKS eşleştirme verileri yüklenirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnConfirm_Click(object sender, RoutedEventArgs e)
        {
            Console.WriteLine($"[PDKS Eşleştirme] Kullanıcı eşleştirmeyi onayladı - {PDKSMatchingRecords.Count} kayıt");
            DialogResult = true;
            Close();
        }
    }

    public class BooleanToYesNoConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? "Eşleşti" : "Eşleşmedi";
            }
            return "Bilinmiyor";
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
