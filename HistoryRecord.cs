using System;

namespace WebScraper
{
    public class HistoryRecord
    {
        public DateTime ProcessDate { get; set; }
        public string ProcessType { get; set; } = "";
        public string Period { get; set; } = "";
        public string Id { get; set; } = "";
        public decimal Amount { get; set; }
        public string Status { get; set; } = "";

        public HistoryRecord()
        {
            ProcessDate = DateTime.Now;
        }

        public HistoryRecord(string processType, string period, string id, decimal amount, string status = "Başarılı")
        {
            ProcessDate = DateTime.Now;
            ProcessType = processType;
            Period = period; // Dönem bilgisini direkt kullan, firma adı ekleme
            Id = id;
            Amount = amount;
            Status = status;
        }
    }
}
