using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace WebScraper
{
    public partial class HolidayCalendarWindow : Window
    {
        private readonly CalendarService _calendarService;
        private readonly CultureInfo _culture = new CultureInfo("tr-TR");
        private bool _isInitializing;

        public int SelectedYear { get; private set; }
        public int SelectedMonth { get; private set; }

        public HolidayCalendarWindow(CalendarService calendarService, int year, int month)
        {
            _calendarService = calendarService ?? throw new ArgumentNullException(nameof(calendarService));

            InitializeComponent();

            calendarControl.Language = XmlLanguage.GetLanguage(_culture.IetfLanguageTag);

            _isInitializing = true;
            InitializeYearCombo(year);
            InitializeMonthCombo(month);
            _isInitializing = false;

            UpdateCalendar();
        }

        private void InitializeYearCombo(int requestedYear)
        {
            var years = _calendarService.GetSupportedYears().ToList();
            if (!years.Contains(requestedYear))
            {
                years.Add(requestedYear);
                years = years.Distinct().OrderBy(y => y).ToList();
            }

            cmbYears.ItemsSource = years;
            SelectedYear = requestedYear;
            cmbYears.SelectedItem = SelectedYear;
        }

        private void InitializeMonthCombo(int requestedMonth)
        {
            var monthNames = Enumerable.Range(1, 12)
                                       .Select(m => _culture.DateTimeFormat.GetMonthName(m))
                                       .ToList();
            cmbMonths.ItemsSource = monthNames;

            SelectedMonth = Math.Min(Math.Max(requestedMonth, 1), 12);
            cmbMonths.SelectedIndex = SelectedMonth - 1;
        }

        private void UpdateCalendar()
        {
            if (_isInitializing)
            {
                return;
            }

            if (cmbYears.SelectedItem is int year)
            {
                SelectedYear = year;
            }

            if (cmbMonths.SelectedIndex >= 0)
            {
                SelectedMonth = cmbMonths.SelectedIndex + 1;
            }

            var summary = _calendarService.GetMonthSummary(SelectedYear, SelectedMonth);
            calendarControl.DisplayDate = new DateTime(SelectedYear, SelectedMonth, 1);

            txtSummary.Text = $"{_culture.DateTimeFormat.GetMonthName(SelectedMonth)} {SelectedYear} • {summary.TotalDays} gün";

            var holidays = summary.Holidays.OrderBy(h => h.Date).ToList();
            lvHolidays.ItemsSource = holidays;

            if (holidays.Count == 0)
            {
                txtHolidayCount.Text = "Bu ay resmi tatil bulunmuyor.";
                txtHolidayCount.Foreground = System.Windows.Media.Brushes.Gray;
            }
            else
            {
                int halfDayCount = holidays.Count(h => h.IsHalfDay);
                txtHolidayCount.Text = halfDayCount > 0
                    ? $"{holidays.Count} tatilin {halfDayCount} adedi yarım gün."
                    : $"{holidays.Count} resmi tatil var.";
                txtHolidayCount.Foreground = System.Windows.Media.Brushes.Green;
            }
        }

        private void calendarControl_Loaded(object sender, RoutedEventArgs e)
        {
            // Ensure highlighting after the visual tree is ready
            Dispatcher.BeginInvoke(new Action(HighlightHolidays), DispatcherPriority.Loaded);
        }

        private void calendarControl_DisplayDateChanged(object sender, CalendarDateChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(HighlightHolidays), DispatcherPriority.Loaded);
        }

        private void HighlightHolidays()
        {
            try
            {
                var summary = _calendarService.GetMonthSummary(SelectedYear, SelectedMonth);
                var holidayDates = new HashSet<DateTime>(summary.Holidays.Select(h => h.Date.Date));

                foreach (var dayButton in FindVisualChildren<CalendarDayButton>(calendarControl))
                {
                    if (dayButton.DataContext is DateTime date)
                    {
                        DateTime day = date.Date;

                        if (holidayDates.Contains(day) && !dayButton.IsInactive)
                        {
                            dayButton.BorderBrush = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)); // #22C55E
                            dayButton.BorderThickness = new Thickness(2);
                            dayButton.Background = new SolidColorBrush(Color.FromRgb(0xEC, 0xFE, 0xF3)); // #ECFEF3 (soft green)
                        }
                        else
                        {
                            // Reset to default-ish look
                            dayButton.ClearValue(Border.BorderBrushProperty);
                            dayButton.ClearValue(Border.BorderThicknessProperty);
                            dayButton.ClearValue(Control.BackgroundProperty);
                        }
                    }
                }
            }
            catch
            {
                // No-op: highlighting is a visual enhancement; ignore failures
            }
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj == null) yield break;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                if (child is T variable)
                {
                    yield return variable;
                }

                foreach (T childOfChild in FindVisualChildren<T>(child))
                {
                    yield return childOfChild;
                }
            }
        }

        private void cmbYears_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            UpdateCalendar();
        }

        private void cmbMonths_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            UpdateCalendar();
        }

        private void btnConfirm_Click(object sender, RoutedEventArgs e)
        {
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

