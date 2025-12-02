using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WebScraper
{
    public partial class UniqueEmailListModal : Window
    {
        private List<KeywordNotification> _keywords;
        
        public UniqueEmailListModal(List<KeywordNotification> keywords)
        {
            InitializeComponent();
            _keywords = keywords ?? new List<KeywordNotification>();
            LoadEmailList();
        }
        
        private void LoadEmailList()
        {
            spEmailList.Children.Clear();
            
            if (_keywords == null || !_keywords.Any())
            {
                var noDataText = new TextBlock
                {
                    Text = "Henüz mail adresi eklenmemiş.",
                    FontSize = 16,
                    Foreground = new SolidColorBrush(Colors.Gray),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 20, 0, 0)
                };
                spEmailList.Children.Add(noDataText);
                txtEmailCount.Text = "Toplam: 0 Tekil Mail";
                return;
            }
            
            // Tekil mail adreslerini al
            var uniqueEmails = _keywords
                .Where(k => !string.IsNullOrEmpty(k.EmailRecipient))
                .Select(k => k.EmailRecipient)
                .Distinct()
                .OrderBy(email => email)
                .ToList();
            
            txtEmailCount.Text = $"Toplam: {uniqueEmails.Count} Tekil Mail";
            
            for (int i = 0; i < uniqueEmails.Count; i++)
            {
                var email = uniqueEmails[i];
                
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
                    Background = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
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
                
                // Mail adresi
                var emailText = new TextBlock
                {
                    Text = email,
                    FontSize = 14,
                    FontWeight = FontWeights.Normal,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(Colors.Black)
                };
                Grid.SetColumn(emailText, 1);
                grid.Children.Add(emailText);
                
                // Bu mail adresini kullanan kelime sayısı
                var keywordCount = _keywords.Count(k => k.EmailRecipient == email);
                var countText = new TextBlock
                {
                    Text = $"{keywordCount} kelime",
                    FontSize = 12,
                    FontWeight = FontWeights.Normal,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(Colors.Blue),
                    Margin = new Thickness(10, 0, 0, 0)
                };
                Grid.SetColumn(countText, 2);
                grid.Children.Add(countText);
                
                itemBorder.Child = grid;
                spEmailList.Children.Add(itemBorder);
            }
        }
        
        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
