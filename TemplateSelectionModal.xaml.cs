using System.Windows;

namespace WebScraper
{
    public partial class TemplateSelectionModal : Window
    {
        public enum TemplateType
        {
            Worker,
            Contract
        }

        public TemplateType SelectedTemplateType { get; private set; } = TemplateType.Worker;

        public TemplateSelectionModal()
        {
            InitializeComponent();
        }

        private void WorkerTemplate_Click(object sender, RoutedEventArgs e)
        {
            SelectedTemplateType = TemplateType.Worker;
            DialogResult = true;
            Close();
        }

        private void ContractTemplate_Click(object sender, RoutedEventArgs e)
        {
            SelectedTemplateType = TemplateType.Contract;
            DialogResult = true;
            Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}