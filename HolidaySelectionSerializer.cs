using System;
using System.Globalization;

namespace WebScraper
{
    public static class HolidaySelectionSerializer
    {
        private const string HalfToken = "HALF";
        private const string FullToken = "FULL";

        public static string Serialize(DateTime date, bool treatAsHalfDay)
        {
            var datePart = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var token = treatAsHalfDay ? HalfToken : FullToken;
            return $"{datePart}|{token}";
        }

        public static bool TryDeserialize(string entry, out DateTime date, out bool? treatAsHalfDay)
        {
            date = default;
            treatAsHalfDay = null;

            if (string.IsNullOrWhiteSpace(entry))
            {
                return false;
            }

            var parts = entry.Split('|');
            var datePart = parts[0].Trim();

            if (!DateTime.TryParseExact(datePart,
                                        "yyyy-MM-dd",
                                        CultureInfo.InvariantCulture,
                                        DateTimeStyles.None,
                                        out date))
            {
                return false;
            }

            if (parts.Length > 1)
            {
                var flag = parts[1].Trim().ToUpperInvariant();
                if (flag == HalfToken || flag == "H")
                {
                    treatAsHalfDay = true;
                }
                else if (flag == FullToken || flag == "F")
                {
                    treatAsHalfDay = false;
                }
            }

            return true;
        }
    }
}

