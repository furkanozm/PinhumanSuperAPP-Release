using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WebScraper
{
    public class HolidayInfo
    {
        public DateTime Date { get; set; }
        public string Name { get; set; }
        public bool IsHalfDay { get; set; }
        public string DurationText => IsHalfDay ? "½ Gün" : "Tam Gün";
        public string DayName => CultureInfo.GetCultureInfo("tr-TR").DateTimeFormat.GetDayName(Date.DayOfWeek);
    }

    public class CalendarMonthSummary
    {
        public int Year { get; }
        public int Month { get; }
        public int TotalDays { get; }
        public IReadOnlyList<HolidayInfo> Holidays { get; }

        public CalendarMonthSummary(int year, int month, IEnumerable<HolidayInfo> holidays)
        {
            Year = year;
            Month = month;
            TotalDays = DateTime.DaysInMonth(year, month);
            Holidays = holidays?.OrderBy(h => h.Date).ToList() ?? new List<HolidayInfo>();
        }
    }

    public class CalendarService
    {
        private readonly Dictionary<int, List<HolidayInfo>> _holidayMap;

        public CalendarService()
        {
            _holidayMap = BuildHolidayMap();
        }

        public IReadOnlyList<int> GetSupportedYears()
        {
            return _holidayMap.Keys.OrderBy(year => year).ToList();
        }

        public int GetDaysInMonth(int year, int month)
        {
            return DateTime.DaysInMonth(year, month);
        }

        public CalendarMonthSummary GetMonthSummary(int year, int month)
        {
            if (!_holidayMap.TryGetValue(year, out var holidays))
            {
                return new CalendarMonthSummary(year, month, Enumerable.Empty<HolidayInfo>());
            }

            var monthHolidays = holidays.Where(h => h.Date.Month == month);
            return new CalendarMonthSummary(year, month, monthHolidays);
        }

        public IReadOnlyList<HolidayInfo> GetHolidays(int year, int month)
        {
            return GetMonthSummary(year, month).Holidays;
        }

        public IReadOnlyList<HolidayInfo> GetHolidaysForYear(int year)
        {
            if (_holidayMap.TryGetValue(year, out var holidays))
            {
                return holidays.OrderBy(h => h.Date).ToList();
            }

            return Array.Empty<HolidayInfo>();
        }

        private Dictionary<int, List<HolidayInfo>> BuildHolidayMap()
        {
            return new Dictionary<int, List<HolidayInfo>>
            {
                {
                    2024, new List<HolidayInfo>
                    {
                        new HolidayInfo { Date = new DateTime(2024, 1, 1), Name = "Yılbaşı" },
                        new HolidayInfo { Date = new DateTime(2024, 4, 9), Name = "Ramazan Bayramı Arifesi", IsHalfDay = true },
                        new HolidayInfo { Date = new DateTime(2024, 4, 10), Name = "Ramazan Bayramı 1. Gün" },
                        new HolidayInfo { Date = new DateTime(2024, 4, 11), Name = "Ramazan Bayramı 2. Gün" },
                        new HolidayInfo { Date = new DateTime(2024, 4, 12), Name = "Ramazan Bayramı 3. Gün" },
                        new HolidayInfo { Date = new DateTime(2024, 4, 23), Name = "Ulusal Egemenlik ve Çocuk Bayramı" },
                        new HolidayInfo { Date = new DateTime(2024, 5, 1), Name = "Emek ve Dayanışma Günü" },
                        new HolidayInfo { Date = new DateTime(2024, 5, 19), Name = "Atatürk'ü Anma, Gençlik ve Spor Bayramı" },
                        new HolidayInfo { Date = new DateTime(2024, 6, 15), Name = "Kurban Bayramı Arifesi", IsHalfDay = true },
                        new HolidayInfo { Date = new DateTime(2024, 6, 16), Name = "Kurban Bayramı 1. Gün" },
                        new HolidayInfo { Date = new DateTime(2024, 6, 17), Name = "Kurban Bayramı 2. Gün" },
                        new HolidayInfo { Date = new DateTime(2024, 6, 18), Name = "Kurban Bayramı 3. Gün" },
                        new HolidayInfo { Date = new DateTime(2024, 6, 19), Name = "Kurban Bayramı 4. Gün" },
                        new HolidayInfo { Date = new DateTime(2024, 7, 15), Name = "Demokrasi ve Milli Birlik Günü" },
                        new HolidayInfo { Date = new DateTime(2024, 8, 30), Name = "Zafer Bayramı" },
                        new HolidayInfo { Date = new DateTime(2024, 10, 28), Name = "Cumhuriyet Bayramı Arifesi", IsHalfDay = true },
                        new HolidayInfo { Date = new DateTime(2024, 10, 29), Name = "Cumhuriyet Bayramı" }
                    }
                },
                {
                    2025, new List<HolidayInfo>
                    {
                        new HolidayInfo { Date = new DateTime(2025, 1, 1), Name = "Yılbaşı" },
                        new HolidayInfo { Date = new DateTime(2025, 3, 29), Name = "Ramazan Bayramı Arifesi", IsHalfDay = true },
                        new HolidayInfo { Date = new DateTime(2025, 3, 30), Name = "Ramazan Bayramı 1. Gün" },
                        new HolidayInfo { Date = new DateTime(2025, 3, 31), Name = "Ramazan Bayramı 2. Gün" },
                        new HolidayInfo { Date = new DateTime(2025, 4, 1), Name = "Ramazan Bayramı 3. Gün" },
                        new HolidayInfo { Date = new DateTime(2025, 4, 23), Name = "Ulusal Egemenlik ve Çocuk Bayramı" },
                        new HolidayInfo { Date = new DateTime(2025, 5, 1), Name = "Emek ve Dayanışma Günü" },
                        new HolidayInfo { Date = new DateTime(2025, 5, 19), Name = "Atatürk'ü Anma, Gençlik ve Spor Bayramı" },
                        new HolidayInfo { Date = new DateTime(2025, 6, 5), Name = "Kurban Bayramı Arifesi", IsHalfDay = true },
                        new HolidayInfo { Date = new DateTime(2025, 6, 6), Name = "Kurban Bayramı 1. Gün" },
                        new HolidayInfo { Date = new DateTime(2025, 6, 7), Name = "Kurban Bayramı 2. Gün" },
                        new HolidayInfo { Date = new DateTime(2025, 6, 8), Name = "Kurban Bayramı 3. Gün" },
                        new HolidayInfo { Date = new DateTime(2025, 6, 9), Name = "Kurban Bayramı 4. Gün" },
                        new HolidayInfo { Date = new DateTime(2025, 7, 15), Name = "Demokrasi ve Milli Birlik Günü" },
                        new HolidayInfo { Date = new DateTime(2025, 8, 30), Name = "Zafer Bayramı" },
                        new HolidayInfo { Date = new DateTime(2025, 10, 28), Name = "Cumhuriyet Bayramı Arifesi", IsHalfDay = true },
                        new HolidayInfo { Date = new DateTime(2025, 10, 29), Name = "Cumhuriyet Bayramı" }
                    }
                },
                {
                    2026, new List<HolidayInfo>
                    {
                        new HolidayInfo { Date = new DateTime(2026, 1, 1), Name = "Yılbaşı" },
                        new HolidayInfo { Date = new DateTime(2026, 3, 19), Name = "Ramazan Bayramı Arifesi", IsHalfDay = true },
                        new HolidayInfo { Date = new DateTime(2026, 3, 20), Name = "Ramazan Bayramı 1. Gün" },
                        new HolidayInfo { Date = new DateTime(2026, 3, 21), Name = "Ramazan Bayramı 2. Gün" },
                        new HolidayInfo { Date = new DateTime(2026, 3, 22), Name = "Ramazan Bayramı 3. Gün" },
                        new HolidayInfo { Date = new DateTime(2026, 4, 23), Name = "Ulusal Egemenlik ve Çocuk Bayramı" },
                        new HolidayInfo { Date = new DateTime(2026, 5, 1), Name = "Emek ve Dayanışma Günü" },
                        new HolidayInfo { Date = new DateTime(2026, 5, 19), Name = "Atatürk'ü Anma, Gençlik ve Spor Bayramı" },
                        new HolidayInfo { Date = new DateTime(2026, 5, 26), Name = "Kurban Bayramı Arifesi", IsHalfDay = true },
                        new HolidayInfo { Date = new DateTime(2026, 5, 27), Name = "Kurban Bayramı 1. Gün" },
                        new HolidayInfo { Date = new DateTime(2026, 5, 28), Name = "Kurban Bayramı 2. Gün" },
                        new HolidayInfo { Date = new DateTime(2026, 5, 29), Name = "Kurban Bayramı 3. Gün" },
                        new HolidayInfo { Date = new DateTime(2026, 5, 30), Name = "Kurban Bayramı 4. Gün" },
                        new HolidayInfo { Date = new DateTime(2026, 7, 15), Name = "Demokrasi ve Milli Birlik Günü" },
                        new HolidayInfo { Date = new DateTime(2026, 8, 30), Name = "Zafer Bayramı" },
                        new HolidayInfo { Date = new DateTime(2026, 10, 28), Name = "Cumhuriyet Bayramı Arifesi", IsHalfDay = true },
                        new HolidayInfo { Date = new DateTime(2026, 10, 29), Name = "Cumhuriyet Bayramı" }
                    }
                },
                {
                    2027, new List<HolidayInfo>
                    {
                        new HolidayInfo { Date = new DateTime(2027, 1, 1), Name = "Yılbaşı" },
                        new HolidayInfo { Date = new DateTime(2027, 3, 8), Name = "Ramazan Bayramı Arifesi", IsHalfDay = true },
                        new HolidayInfo { Date = new DateTime(2027, 3, 9), Name = "Ramazan Bayramı 1. Gün" },
                        new HolidayInfo { Date = new DateTime(2027, 3, 10), Name = "Ramazan Bayramı 2. Gün" },
                        new HolidayInfo { Date = new DateTime(2027, 3, 11), Name = "Ramazan Bayramı 3. Gün" },
                        new HolidayInfo { Date = new DateTime(2027, 4, 23), Name = "Ulusal Egemenlik ve Çocuk Bayramı" },
                        new HolidayInfo { Date = new DateTime(2027, 5, 1), Name = "Emek ve Dayanışma Günü" },
                        new HolidayInfo { Date = new DateTime(2027, 5, 19), Name = "Atatürk'ü Anma, Gençlik ve Spor Bayramı" },
                        new HolidayInfo { Date = new DateTime(2027, 5, 15), Name = "Kurban Bayramı Arifesi", IsHalfDay = true },
                        new HolidayInfo { Date = new DateTime(2027, 5, 16), Name = "Kurban Bayramı 1. Gün" },
                        new HolidayInfo { Date = new DateTime(2027, 5, 17), Name = "Kurban Bayramı 2. Gün" },
                        new HolidayInfo { Date = new DateTime(2027, 5, 18), Name = "Kurban Bayramı 3. Gün" },
                        new HolidayInfo { Date = new DateTime(2027, 5, 19), Name = "Kurban Bayramı 4. Gün" },
                        new HolidayInfo { Date = new DateTime(2027, 7, 15), Name = "Demokrasi ve Milli Birlik Günü" },
                        new HolidayInfo { Date = new DateTime(2027, 8, 30), Name = "Zafer Bayramı" },
                        new HolidayInfo { Date = new DateTime(2027, 10, 28), Name = "Cumhuriyet Bayramı Arifesi", IsHalfDay = true },
                        new HolidayInfo { Date = new DateTime(2027, 10, 29), Name = "Cumhuriyet Bayramı" }
                    }
                },
                {
                    2028, new List<HolidayInfo>
                    {
                        new HolidayInfo { Date = new DateTime(2028, 1, 1), Name = "Yılbaşı" },
                        new HolidayInfo { Date = new DateTime(2028, 2, 26), Name = "Ramazan Bayramı Arifesi", IsHalfDay = true },
                        new HolidayInfo { Date = new DateTime(2028, 2, 27), Name = "Ramazan Bayramı 1. Gün" },
                        new HolidayInfo { Date = new DateTime(2028, 2, 28), Name = "Ramazan Bayramı 2. Gün" },
                        new HolidayInfo { Date = new DateTime(2028, 2, 29), Name = "Ramazan Bayramı 3. Gün" },
                        new HolidayInfo { Date = new DateTime(2028, 4, 23), Name = "Ulusal Egemenlik ve Çocuk Bayramı" },
                        new HolidayInfo { Date = new DateTime(2028, 5, 1), Name = "Emek ve Dayanışma Günü" },
                        new HolidayInfo { Date = new DateTime(2028, 5, 19), Name = "Atatürk'ü Anma, Gençlik ve Spor Bayramı" },
                        new HolidayInfo { Date = new DateTime(2028, 5, 5), Name = "Kurban Bayramı Arifesi", IsHalfDay = true },
                        new HolidayInfo { Date = new DateTime(2028, 5, 6), Name = "Kurban Bayramı 1. Gün" },
                        new HolidayInfo { Date = new DateTime(2028, 5, 7), Name = "Kurban Bayramı 2. Gün" },
                        new HolidayInfo { Date = new DateTime(2028, 5, 8), Name = "Kurban Bayramı 3. Gün" },
                        new HolidayInfo { Date = new DateTime(2028, 5, 9), Name = "Kurban Bayramı 4. Gün" },
                        new HolidayInfo { Date = new DateTime(2028, 7, 15), Name = "Demokrasi ve Milli Birlik Günü" },
                        new HolidayInfo { Date = new DateTime(2028, 8, 30), Name = "Zafer Bayramı" },
                        new HolidayInfo { Date = new DateTime(2028, 10, 28), Name = "Cumhuriyet Bayramı Arifesi", IsHalfDay = true },
                        new HolidayInfo { Date = new DateTime(2028, 10, 29), Name = "Cumhuriyet Bayramı" }
                    }
                }
            };
        }
    }
}

