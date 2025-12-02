using System;
using System.Collections.Generic;

namespace WebScraper
{
    public class PersonnelHistoryRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string TCKN { get; set; } = "";
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string Gender { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Email { get; set; } = "";
        public string BankName { get; set; } = "";
        public string AccountName { get; set; } = "";
        public string IBAN { get; set; } = "";
        public string PersonelTipi { get; set; } = "İşçi"; // İşçi, Sözleşmeli Pers.
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public string Status { get; set; } = "Başarılı"; // Başarılı, Başarısız
        public string Notes { get; set; } = "";
    }

    public class PersonnelHistory
    {
        public List<PersonnelHistoryRecord> Records { get; set; } = new List<PersonnelHistoryRecord>();

    }
}
