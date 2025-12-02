using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace WebScraper
{
	public partial class SelectCreatorsWindow : Window
	{
		public List<string> SelectedCreators { get; private set; } = new List<string>();

		public SelectCreatorsWindow(IEnumerable<string> creators)
		{
			InitializeComponent();
			lstCreators.ItemsSource = creators;
			
			// Pencereyi her zaman en öne getir
			this.Topmost = true;
			this.Activate();
			this.Focus();
			
			// Pencere yüklendiğinde de en öne getir
			this.Loaded += (s, e) =>
			{
				this.Topmost = true;
				this.Activate();
				this.Focus();
			};
		}

		private void btnOk_Click(object sender, RoutedEventArgs e)
		{
			SelectedCreators = lstCreators.SelectedItems.Cast<string>().ToList();
			DialogResult = true;
			Close();
		}

		private void btnCancel_Click(object sender, RoutedEventArgs e)
		{
			DialogResult = false;
			Close();
		}

		private void btnSelectAll_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				// Tüm öğeleri seç
				lstCreators.SelectAll();
				
				// Eğer SelectAll çalışmazsa manuel olarak seç
				if (lstCreators.SelectedItems.Count == 0)
				{
					for (int i = 0; i < lstCreators.Items.Count; i++)
					{
						lstCreators.SelectedItems.Add(lstCreators.Items[i]);
					}
				}
			}
			catch (System.Exception ex)
			{
				// Hata durumunda manuel seçim yap
				for (int i = 0; i < lstCreators.Items.Count; i++)
				{
					lstCreators.SelectedItems.Add(lstCreators.Items[i]);
				}
			}
		}
	}
} 