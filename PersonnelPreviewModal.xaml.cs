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
    public partial class PersonnelPreviewModal : Window
    {
        public List<PersonnelRecord> PersonnelRecords { get; private set; }

        public PersonnelPreviewModal(List<PersonnelRecord> personnelRecords)
        {
            InitializeComponent();
            PersonnelRecords = personnelRecords ?? new List<PersonnelRecord>();
            LoadPersonnelData();
        }

        private void LoadPersonnelData()
        {
            try
            {
                dgPersonnelList.ItemsSource = PersonnelRecords;

                // Kayıt sayısını göster
                txtRecordCount.Text = $"Toplam {PersonnelRecords.Count} kayıt bulundu";

                Console.WriteLine($"[Personel Önizleme] {PersonnelRecords.Count} personel kaydı modal'da gösteriliyor");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Personel Önizleme] Veri yükleme hatası: {ex.Message}");
                MessageBox.Show($"Personel verileri yüklenirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnConfirm_Click(object sender, RoutedEventArgs e)
        {
            Console.WriteLine($"[Personel Önizleme] Kullanıcı verileri onayladı - {PersonnelRecords.Count} kayıt");
            DialogResult = true;
            Close();
        }
    }
}
