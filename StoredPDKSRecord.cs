using System;

namespace WebScraper
{
    /// <summary>
    /// Kayıtlı PDKS verileri için günlük kayıt modeli
    /// Her satır bir personelin bir gününü temsil eder
    /// </summary>
    public class StoredPDKSRecord
    {
        public string PersonnelCode { get; set; } = string.Empty;
        public string PersonnelName { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public TimeSpan CheckInTime { get; set; }
        public TimeSpan CheckOutTime { get; set; }
        public string ShiftType { get; set; } = string.Empty;
        public bool IsSelected { get; set; } = false;
    }
}

