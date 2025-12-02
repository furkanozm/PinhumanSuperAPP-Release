using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WebScraper
{
    public partial class KeywordListModal : Window
    {
        private List<KeywordNotification> _keywords;
        
        public KeywordListModal(List<KeywordNotification> keywords)
        {
            InitializeComponent();
            _keywords = keywords ?? new List<KeywordNotification>();
            LoadKeywordList();
        }
        
        private void LoadKeywordList()
        {
            spKeywordList.Children.Clear();
            
            if (_keywords == null || !_keywords.Any())
            {
                var noDataText = new TextBlock
                {
                    Text = "Henüz kelime eklenmemiş.",
                    FontSize = 16,
                    Foreground = new SolidColorBrush(Colors.Gray),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 20, 0, 0)
                };
                spKeywordList.Children.Add(noDataText);
                txtKeywordCount.Text = "Toplam: 0 Kelime";
                return;
            }
            
            txtKeywordCount.Text = $"Toplam: {_keywords.Count} Kelime";
            
            for (int i = 0; i < _keywords.Count; i++)
            {
                var keyword = _keywords[i];
                
                var itemBorder = new Border
                {
                    Background = new SolidColorBrush(Colors.White),
                    BorderBrush = new SolidColorBrush(Colors.LightGray),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 0, 0, 5)
                };
                
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                
                // Sıra numarası
                var indexBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(33, 150, 243)),
                    CornerRadius = new CornerRadius(12),
                    Width = 28,
                    Height = 28,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                
                var indexText = new TextBlock
                {
                    Text = (i + 1).ToString(),
                    Foreground = new SolidColorBrush(Colors.White),
                    FontSize = 10,
                    FontWeight = FontWeights.Normal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.NoWrap
                };
                
                indexBadge.Child = indexText;
                Grid.SetColumn(indexBadge, 0);
                grid.Children.Add(indexBadge);
                
                // Kelime adı
                var keywordText = new TextBlock
                {
                    Text = keyword.Keyword,
                    FontSize = 14,
                    FontWeight = FontWeights.Normal,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(Colors.Black)
                };
                Grid.SetColumn(keywordText, 1);
                grid.Children.Add(keywordText);
                
                // Durum
                var statusText = new TextBlock
                {
                    Text = keyword.Enabled ? "✅ Aktif" : "❌ Pasif",
                    FontSize = 12,
                    FontWeight = FontWeights.Normal,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(keyword.Enabled ? Colors.Green : Colors.Red),
                    Margin = new Thickness(10, 0, 0, 0)
                };
                Grid.SetColumn(statusText, 2);
                grid.Children.Add(statusText);
                
                itemBorder.Child = grid;
                spKeywordList.Children.Add(itemBorder);
            }
        }
        
        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
